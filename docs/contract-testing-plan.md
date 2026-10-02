# Contract Testing — план реализации

**Статус:** Фазы A, B, C и E реализованы; D — не начата.
**Контекст:** детализация Фазы 04 из `docs/project-brief.md` («Contract testing + полировка»). Тот документ фиксирует *что* и *зачем* на уровне продукта; этот — *как*, на уровне доменной модели, компонентов и шагов реализации. Правила структуры/стиля кода — как обычно, в `.claude/CLAUDE.md`.

## 1. Четыре вида тестов

Со слов заказчика, нужны четыре разных сценария. Ниже — как они называются в контракт-тестинге вообще (Microcks, Pact и т.п.), чтобы не изобретать термины по ходу реализации.

| # | Описание пользователя | Термин | Направление | Валидация |
|---|---|---|---|---|
| 1 | Шлём HTTP-запрос к сервису, проверяем что ответ соответствует спеке | **Consumer test (request/response)** | мы → сервис (HTTP) | тело **ответа** против response-схемы операции |
| 2 | Шлём сообщение в брокер, просто отправили и всё | **Producer test (fire-and-forget)** | мы → брокер | нет (только факт доставки/паблиша) |
| 3 | Сервис шлёт нам HTTP-запрос на путь из спеки, мы отвечаем по спеке; проверяем что их запрос соответствует спеке | **Provider test (request/response)** | сервис → мы (HTTP) | тело **входящего запроса** против request-схемы операции |
| 4 | Ждём сообщение в брокере, валидируем по схеме | **Provider test (async, listen)** | брокер → мы | тело сообщения против payload-схемы AsyncAPI |

Важное структурное отличие внутри четвёрки: **№1, №2, №4 — тесты «по требованию»** (пользователь жмёт «Run», получает один результат за один прогон). **№3 — пассивный/постоянный** режим: как только мок включён в этом режиме, каждый реальный входящий вызов на него автоматически логируется и валидируется — это не «одна кнопка Run», а переключатель состояния мок-эндпоинта. Это должно повлиять на UX и модель данных (см. раздел 4).

## 2. Что уже реализовано

### 2.1 Общая инфраструктура (переиспользуется всеми четырьмя типами)

- **`ApiSpecification`/`MockEndpoint`** (`VroksNet.Domain`) — распарсенные операции спеки, `OperationKey` (`"GET /pets"` для OpenAPI, `"orders.created:send"` для AsyncAPI — см. `VroksNet.Domain.TestScenarios.OperationCompatibility`), `ExampleTemplate`.
- **`Connection`/`ConnectionServiceType`** — именованные подключения (Http/RabbitMq/Nats), CRUD на странице Settings, `IConnectionTester`/`ConnectionTester` — проверка доступности (не путать с валидацией контракта).
- **`TestScenario`** (`VroksNet.Domain.TestScenarios`) — сохранённый сценарий «операция X спеки → Connection Y», CRUD + `RunTestScenarioHandler`, UI на странице Test Scenarios.
- **`IMessageSender`/`MessageSender`** (`VroksNet.Infrastructure.Connections`) — реальная отправка: HTTP-запрос (для `Http`-connection) или publish в RabbitMQ/NATS (для `RabbitMq`/`Nats`-connection). Не переиспользует Aspire dev-ресурсы из `AppHost.cs` — открывает соединение по данным, введённым в `Connection.Value`.
- **`CallRecord`/`CallDirection`** — сущность для истории вызовов. `CallDirection` уже включает `InboundHttpRequest`, `OutboundBrokerPublish`, `OutboundHttpRequest`. После Фазы E история читается (`GET /api/call-records`, страница Call History), и её пишут оба источника: `RunTestScenarioHandler` (`OutboundHttpRequest`/`OutboundBrokerPublish`) и `InvokeMockEndpointHandler` (`InboundHttpRequest`).

### 2.2 Тип 1 (Consumer, request/response) — ✅ реализовано (Фаза B)

`TestScenario` + `RunTestScenarioHandler` + `MessageSender` (HTTP-ветка) отправляют запрос операции через Http-connection, получают статус+тело и проверяют ответ против схемы, объявленной в спеке для **полученного** статуса. Ответ не по спеке валит прогон, даже если HTTP-запрос прошёл. Подробности — в 4.2.

### 2.3 Тип 2 (Producer, fire-and-forget) — реализовано полностью

`TestScenario` + `MessageSender` (RabbitMq/Nats-ветка): `ConnectionFactory.CreateConnectionAsync` + `BasicPublishAsync` для RabbitMQ, `NatsConnection.PublishAsync` для NATS. Ответа не ждём — это соответствует описанию «отправляем и всё». Валидация тут по определению не нужна (fire-and-forget), так что дополнительной работы не требуется.

### 2.4 Тип 3 (Provider, request/response) — реализовано частично, с существенным пробелом

`MockInvocationEndpoints` (`POST/GET/... /mock/{**path}`) + `OperationKeyMatcher` + `InvokeMockEndpointHandler` — matching по методу и пути (без query/body), отдают `ExampleTemplate`. Три пробела относительно того, что описал заказчик:

1. **Путь.** Мок сейчас живёт под префиксом `/mock/...`, а не на «именно том пути, который указан в спецификации» — реальный сервис, который настроен стучаться на `/orders`, никогда не попадёт на `/mock/orders` сам по себе. Нужен режим раздачи на реальном пути (см. 4.4; риски — в 3.5).
2. **Валидация входящего запроса.** Сейчас `InvokeMockEndpointHandler` вообще не смотрит на тело запроса — только матчит операцию и отдаёт статичный пример. Схема (`MockEndpoint.RequestSchema`) и валидатор (`ISchemaValidator`) после Фазы A есть, но здесь не вызываются.
3. ~~**История.**~~ ✅ Закрыто в Фазе E: каждый вызов мока, включая несовпавшие (404), пишет `CallRecord` (`InboundHttpRequest`, `RequestLine`, тело запроса/ответа, статус). Валидации входящего запроса пока нет — это Фаза D.

### 2.5 Тип 4 (Provider, async listen) — ✅ реализовано (Фаза C)

`TestScenario` в режиме `Listen` ждёт следующее сообщение на канале операции через `IMessageListener` (RabbitMQ/NATS) и валидирует его по payload-схеме AsyncAPI. Подробности — в 4.3.

### 2.6 Схемы операций — ✅ закрыто Фазой A

Раньше парсеры видели полную JSON Schema, но `ParsedOperation`/`MockEndpoint` сохраняли только `ExampleJson`/`ExampleTemplate`, а схему выбрасывали — без неё не валидировался ни один из Типов 1/3/4. Теперь схемы извлекаются и хранятся в `MockEndpoint.RequestSchema`/`ResponseSchema` (для AsyncAPI payload-схема лежит в `ResponseSchema`), есть `ISchemaValidator`. Подробности — в 4.1. Тип 1 их уже использует (Фаза B, по схемам всех статусов — см. 4.2); Типы 3/4 — Фазы C–D.

## 3. Открытые решения (нужно подтвердить до реализации)

1. ~~**Библиотека JSON Schema.**~~ **Решено и сделано (Фаза A):** [`JsonSchema.Net`](https://github.com/gregsdennis/json-everything) 7.3.4, пинована в `Directory.Packages.props`, используется только внутри `SchemaValidator` (`VroksNet.Infrastructure.SchemaValidation`) — наружу течёт исключительно через `ISchemaValidator`.
2. ~~**Где хранить схему.**~~ **Решено и сделано (Фаза A):** на импорте — `MockEndpoint.RequestSchema`/`ResponseSchema` (nullable `string`, миграция `AddMockEndpointSchemas`), заполняются `ImportOpenApiSpecHandler`/`ImportAsyncApiSpecHandler` из `ParsedOperation.RequestSchemaJson`/`ResponseSchemaJson`. Для AsyncAPI `ResponseSchemaJson` несёт схему payload'а сообщения (решение по пункту ниже про "одно поле вместо трёх" — см. 4.1).
3. ~~**Модель данных для «Слушать» (Тип 4).**~~ **Решено (Фаза C):** `TestScenario` расширен полем `Kind` — `Send` (HTTP-запрос или публикация, как раньше) / `Listen` — плюс `ListenTimeoutSeconds` и `ListenExchange`. HTTP vs брокер по-прежнему определяется типом connection, отдельные `SendHttp`/`PublishBroker` не нужны. Режим нового сценария по умолчанию берётся из `action` операции (см. 4.3), пользователь может переключить; существующие сценарии остались `Send`.
4. **Модель для пассивного Provider-режима (Тип 3).** Это НЕ «сценарий, который прогоняют» — это состояние мок-эндпоинта. Предлагается булев флаг на `MockEndpoint` (например `ServeAtRealPath` + `ValidateIncomingRequests`, можно объединить в один) в дополнение к существующему `IsEnabled`, а не отдельная сущность.
5. **Коллизии реальных путей.** Если два импортированных спеки объявляют одинаковый путь (`/health` у обоих), и оба включат «раздавать на реальном пути» — конфликт. Нужна политика: либо запрет на включение флага при коллизии (проверка при выставлении флага), либо явный приоритет (например, последний включённый побеждает — рискованно). Отдельный риск: реальный путь может случайно перекрыть существующий маршрут `ApiService` (`/api/...`, `/health`, `/`) — нужен guard-список зарезервированных префиксов.
6. **История/отчётность.** Без read-стороны у `ICallRecordRepository` результаты Типа 3 (и отчасти Типа 4, если сохранять историю прогонов) физически некуда посмотреть, кроме как через сам ответ одного Run. Нужна страница «История вызовов» (уже заявлена в MVP, раздел 2 `project-brief.md`) — реализация read-стороны репозитория и минимального списка/фильтра. Решено: делается Фазой E (см. 4.5).
7. ~~**Хранение результата валидации в `CallRecord`.**~~ **Решено:** поля на самой записи — `bool? ContractValid` (`null` = схемы не было / валидация не применялась) + `string? ValidationErrors` (JSON-массив сообщений из `SchemaValidationResult`). Общее для Типов 1/3/4. Отдельную таблицу ошибок не заводим — ошибки читаются только вместе с записью. Поля добавлены миграцией Фазы B (`AddResponseSchemasByStatusAndCallRecordContract`, вместе со `StatusCode`/`TestScenarioId` из 4.5) — Тип 1 их уже заполняет, Фазы C/D будут заполнять так же.

## 4. Целевая архитектура

### 4.1 Фаза A — фундамент: схемы + валидатор (без изменения поведения) — ✅ реализовано

- `ParsedOperation` (`ISpecificationParser`) расширена схемами (оба новых параметра — с дефолтом `null`, чтобы не ломать существующие места создания):
  ```
  ParsedOperation(string OperationKey, string? ExampleJson, string? RequestSchemaJson = null, string? ResponseSchemaJson = null)
  ```
  `OpenApiSpecificationParser` берёт `RequestSchemaJson` из `requestBody.content["application/json"].schema`, `ResponseSchemaJson` — из схемы первого JSON-ответа (тот же response, из которого берётся example) — оба сериализуются через `IOpenApiSchema.SerializeAsV31(...)` с `OpenApiWriterSettings { InlineLocalReferences = true }`, так что локальные `$ref` на `#/components/schemas/...` разворачиваются в текст самой схемы (иначе схема невалидна сама по себе вне контекста всего документа). `AsyncApiSpecificationParser` кладёт payload-схему сообщения в `ResponseSchemaJson` (решение принято — не заводить третье поле под HTTP-нейтральный термин), тем же способом резолвя один `$ref`-хоп на `components.schemas.*` вручную (как уже делалось для example).
- `MockEndpoint` получил `RequestSchema`/`ResponseSchema` (nullable `string`) — миграция `20260909105919_AddMockEndpointSchemas`. `ImportOpenApiSpecHandler`/`ImportAsyncApiSpecHandler` копируют их из `ParsedOperation`. **Пока нигде не отдаются наружу** (не в `MockEndpointDetail`/API/UI) — это по плану, поведение не меняется до Фазы B.
- `ISchemaValidator`/`SchemaValidationResult` в `Application/Abstractions`; реализация — `VroksNet.Infrastructure.SchemaValidation.SchemaValidator` на `JsonSchema.Net` (`EvaluationOptions { OutputFormat = OutputFormat.List }`, чтобы получить плоский список сообщений об ошибках, а не только «не прошло»).
- Тесты: `OpenApiSpecificationParserTests`/`AsyncApiSpecificationParserTests` дополнены проверками, что схема извлекается и `$ref` действительно разворачивается (`Assert.DoesNotContain("$ref", ...)`); новый `SchemaValidatorTests` — чистые юнит-тесты валидатора (валидный/невалидный instance, неправильный тип, нарушение `minimum`, битый JSON) — без Docker, как и остальная такая инфраструктура (`ConnectionTesterTests`/`MessageSenderTests`).

### 4.2 Фаза B — Тип 1: валидация ответа при Send — ✅ реализовано

Принятые решения:
- **Схема выбирается по фактическому статусу ответа**, а не одна «схема ответа» на операцию. Иначе 404/500 со своей (корректной) схемой валидировались бы против схемы 200 и давали ложные ошибки.
- **Итог прогона = транспорт И контракт.** `Success`/`LastRunSuccess` = `false`, если ответ не соответствует спеке. Что именно упало, видно по `ContractValidation`.

Реализация:
- `OpenApiSpecificationParser` собирает схемы **всех** объявленных ответов в `ParsedOperation.ResponseSchemasByStatus`: ключ — статус-ключ OpenAPI (`"200"`, `"4XX"` (range-ключи приводятся к верхнему регистру), `"default"`), значение — self-contained JSON-схема тела или `null`, если ответ объявлен без JSON-тела. Хранится в `MockEndpoint.ResponseSchemasByStatus` (одна JSON-колонка через value converter). `ResponseSchema` остаётся как был (первый JSON-ответ / payload AsyncAPI) — для Фаз C/D.
- Правило выбора — в Domain, `MockEndpoint.TryGetDeclaredResponse(statusCode, out schema)`: точный код → range (`"2XX"`) → `"default"`, как в спецификации OpenAPI.
- `MessageSendResult` получил `int? StatusCode`; `MessageSender` его заполняет для HTTP.
- `RunTestScenarioHandler` (принимает `ISchemaValidator`) для успешного HTTP-ответа:
  - нет объявленных ответов (AsyncAPI, или OpenAPI-спека, импортированная до Фазы B) → валидация пропускается (`ContractValidation = null`). **Старые спеки нужно переимпортировать**, чтобы для них заработала валидация;
  - статус не объявлен в спеке → нарушение контракта (`"Status 500 isn't declared for GET /pets in the spec."`);
  - статус объявлен без JSON-тела → контракт соблюдён, тело не проверяется;
  - объявлена схема, а тело пустое → нарушение; иначе → `ISchemaValidator.Validate(schema, body)`.
- `RunTestScenarioResult` получил `StatusCode` и `ContractValidation` (`SchemaValidationResult?`). `Message` при нарушении дополняется «— response doesn't match the spec (N violation(s))».
- `CallRecord` заполняет `StatusCode`, `TestScenarioId`, `ContractValid`, `ValidationErrors` (JSON-массив).
- UI (Test Scenarios): под результатом Run — отдельный бейдж «Matches spec» / «Contract violated» со списком ошибок (`role="status"`, `aria-label="Contract check"`).
- Тесты: юнит — хендлер (совпадение, нарушение, схема своего статуса, range/default, необъявленный статус, статус без тела, пустое тело, нет объявленных ответов, провал отправки), парсер (fixture `response-statuses-openapi.yaml`), round-trip словаря через реальный SQLite; интеграционный — прогон против собственного `GET /api/connections` ApiService со схемой-массивом (проходит) и схемой-объектом (нарушение); E2E — бейдж «Contract violated» на странице Test Scenarios.

### 4.3 Фаза C — Тип 4: Listen & validate — ✅ реализовано

Принятые решения:
- **Режим — явное поле `TestScenario.Kind` с дефолтом по `action`** (см. 3.3). AsyncAPI-операция `action: send` — это то, что публикует сам описанный сервис, поэтому её проверка — слушать (`Listen`); `action: receive` (сервис потребляет) и HTTP — отправлять (`Send`). Правило — `TestScenarioListening.DefaultKindFor` в Domain; сервер отдаёт его как `MockEndpointDetail.DefaultTestScenarioKind`, форма подставляет при выборе операции.
- **RabbitMQ слушается своей временной очередью на exchange**, а не чтением очереди канала: серверно-именованная exclusive + auto-delete очередь, привязанная к exchange с binding key = адрес канала. Реальные потребители получают свою копию, ничего не «крадётся». Exchange задаётся в сценарии (`ListenExchange`), по умолчанию `amq.topic`. Следствие: сервис должен публиковать в exchange — сообщение, отправленное напрямую в очередь через default exchange (как это делает наш `MessageSender`), так не подслушать.
- NATS — обычная core-подписка; после неё `PING`, чтобы подписка точно была зарегистрирована до начала ожидания.
- **Параметры каналов.** Подписка идёт по шаблону: сегмент адреса, целиком являющийся параметром AsyncAPI (`orders.{region}.created`), становится wildcard'ом `*` (одинаково в RabbitMQ topic и NATS). Если параметр — часть сегмента или адрес разделён `/` (`user/{id}/signedup`), подписаться нельзя: такой сценарий не сохраняется (`CanListen` = false). На fanout/headers-exchange binding key игнорируется — засчитается любое сообщение exchange'а.
- **Ограничение:** Listen не слышит наш собственный Send на той же операции — Send публикует в default exchange, к которому нельзя привязать очередь. Опция «exchange для Send» — отдельная задача.

Реализация:
- `IMessageListener.ListenAsync(connection, operationKey, timeout, exchange)` → `MessageListenResult(Received, Message, Payload)`; `MessageListener` (Infrastructure) — короткоживущий, подключение/очередь/подписка живут только на время прогона. Подключение и настройка подписки (10s) отделены от таймаута ожидания, у каждого этапа своё сообщение; отсутствующий exchange — понятная ошибка. Соединения RabbitMQ открываются через общий `RabbitMqConnections` (таймауты клиента, без автовосстановления, отмена по токену) — им же теперь пользуются `ConnectionTester`/`MessageSender`, иначе брошенное по таймауту подключение утекало.
- Таймаут ожидания: 1–80 секунд, по умолчанию 30 (`TestScenarioListening`). Верхняя граница — чтобы прогон (ожидание + до 10s на подключение и настройку), который держит HTTP-запрос открытым, укладывался в таймаут HttpClient'а Admin UI (100s).
- `RunTestScenarioHandler` ветвится по `Kind`: `Listen` → слушатель → (если получено и у операции есть payload-схема) `ISchemaValidator`. Не получено за таймаут — провал прогона без валидации. Пустое сообщение при объявленной схеме — нарушение.
- Новое `CallDirection.InboundBrokerMessage`; запись истории хранит полученное сообщение (или причину неудачи) в `ResponseSnapshot`.
- Валидация на create/update: неизвестный `Kind` отклоняется; `Listen` только для операций с подписываемым каналом, таймаут в диапазоне; exchange хранится только для RabbitMQ-подключения; для `Send` настройки прослушивания отбрасываются. Миграция `AddTestScenarioListenMode`.
- Правила для UI (`RequiresHttpConnection`, `CanListen`, `DefaultTestScenarioKind`) сервер отдаёт в `MockEndpointDetail` — Web их не выводит сам из ключа операции.
- `ChannelAddressOf` (адрес канала из ключа операции) переехал из `MessageSender` в Domain (`OperationCompatibility`) — общий для отправки и прослушивания.
- UI (Test Scenarios): для AsyncAPI-операции — выбор режима; в режиме Listen вместо payload — «Wait up to (seconds)» и (для RabbitMQ-подключения) «Exchange». В таблице — бейдж «Listen», во время прогона — «Listening…». В Call History — «Broker message (in)».
- Тесты: юнит — дефолт режима, валидация create, ветка Listen (получено/нарушение/таймаут/дефолты), `MessageListener` на недоступных брокерах; интеграционные — реальные RabbitMQ (через `amq.topic`, валидное и невалидное сообщение) и NATS, таймаут; E2E — форма предлагает Listen для `send`-операции, прогон с таймаутом 1s сообщает «No message».

### 4.4 Фаза D — Тип 3: Provider-режим (реальный путь + валидация входящих)

Самая рискованная фаза — требует решения по 3.5 до старта.

- Флаг(и) на `MockEndpoint` (см. 3.4), редактируемые из UI (карточка операции на странице спецификации).
- Новый роутинг-слой в `ApiService`, параллельный `MockInvocationEndpoints`: catch-all на корневых путях включённых операций (не под `/mock`). Нужна проверка коллизий при включении флага (см. 3.5) — вероятно, отдельный Application-хендлер `EnableProviderMode(MockEndpointId)`, который проверяет пересечения путей по всем включённым `MockEndpoint` перед тем, как позволить включить.
- `InvokeMockEndpointHandler` (или его аналог для этого роута) валидирует тело входящего запроса против `MockEndpoint.RequestSchema`, пишет `CallRecord` (`Direction = InboundHttpRequest`, `ContractValid`/`ValidationErrors` — см. 3.7) — независимо от результата валидации, ответ всё равно отдаётся по текущей логике (пример / будущий templating из раздела «Динамика ответов» в `project-brief.md`, который тоже пока не реализован — отдельная, не блокирующая эту фазу задача).

### 4.5 Фаза E — История вызовов — ✅ реализовано

Принятые решения:
- **Входящие вызовы мока логируются уже сейчас** (пробел №3 из 2.4), не дожидаясь Фазы D: иначе на странице истории видны только прогоны сценариев. Несовпавшие вызовы (404) тоже пишутся — «почему сервис получил 404?» как раз вопрос к истории.
- **Очистка — явная кнопка «Clear history»** (`DELETE /api/call-records`, с подтверждением в UI). Автоматического лимита/ротации нет.

Реализация:
- `ICallRecordRepository.ListAsync(CallRecordFilter, CallRecordCursor?, limit)` + `DeleteAllAsync`. Фильтры: спека, операция, сценарий, направление, исход контракта. Порядок — новые сверху, по (`Timestamp`, `Id`); пагинация **курсорная (keyset)**, не offset, поэтому новые записи между запросами не сдвигают страницы. `Id` — тай-брейкер для записей с одинаковым временем.
- **`CallRecord.Timestamp` хранится как UTC-тики (`INTEGER`)**, а не ISO-текст по умолчанию: SQLite-провайдер EF Core не умеет `ORDER BY`/сравнение по `DateTimeOffset`, а текстовый порядок неверен при разных offset'ах. Миграция `AddCallHistoryIndexesAndRequestLine` конвертирует существующие текстовые значения SQL-ем (через `julianday`) и обратно в `Down`.
- Новое поле `CallRecord.RequestLine` — для входящих вызовов фактическая строка запроса (`GET /mock/pets/1?x=1`); единственный способ понять, что запрашивали, когда ничего не совпало. Индексы: (`Timestamp`, `Id`) и (`SpecificationId`, `Timestamp`, `Id`) — покрывают весь keyset-порядок без временной сортировки.
- **Тела ограничены и не входят в список.** Сохраняемые тела запроса/ответа обрезаются до 64K символов с маркером `…(truncated)` (`CallRecordSnapshot`); мок-эндпоинт и не читает из запроса больше. Список истории тел не содержит — их отдаёт `GET /api/call-records/{id}` (`GetCallRecord`), UI подгружает их по «Details».
- Application: `ListCallRecords` → `CallRecordPage(Items, NextCursor)` с денормализованными именами (спека/операция/подключение/сценарий, `"(deleted …)"` для удалённых) — их подтягивает `ICallRecordNameResolver` проекциями только по id текущей страницы, без загрузки спек со схемами; `ValidationErrors` разобраны в массив; `limit` по умолчанию 50, максимум 200; битый курсор → `ArgumentException` (как остальная валидация — пока 500). `ClearCallRecords` → число удалённых.
- `InvokeMockEndpointHandler` пишет `CallRecord`; выбор тела ответа (пример или `"{}"`) переехал из эндпоинта в хендлер. Логирование **best-effort**: сбой записи в историю логируется как warning, мок всё равно отвечает. Для 404 в историю пишется текст сообщения, а клиент получает его обёрнутым в ProblemDetails.
- UI: страница **Call History** (`/call-history`, пункт меню): фильтры, таблица (время, вид, операция/строка запроса, сценарий/подключение, статус, контракт), раскрытие деталей (строка запроса, нарушения, тела), «Load older», «Refresh», «Clear history».
- Попутно исправлено: `MessageSender` терял базовый путь и query string подключения (`https://host/v1?api-key=…` + `/pets` уходил на `https://host/pets`) — теперь сохраняются оба.
- Ротации/лимита истории нет — только «Clear history»; при активном использовании мока история растёт (по ≤128K символов тел на запись).
- Опасение из ранней версии плана про «запись появляется не сразу после Run» не подтвердилось: `IDbWriteQueue.EnqueueAsync` ждёт выполнения записи, так что после ответа Run запись уже читается.

## 5. Порядок реализации (рекомендация)

1. ✅ **Фаза A** (схемы + валидатор) — сделано, см. 4.1.
2. ✅ **Фаза B** (Тип 1) — сделано, см. 4.2 (сделана раньше E по решению заказчика; поля `CallRecord` из 3.7 добавлены в её миграции).
3. ✅ **Фаза E** (история) — сделано, см. 4.5.
4. ✅ **Фаза C** (Тип 4) — сделано, см. 4.3.
5. **Фаза D** (Тип 3) — самая рискованная (реальные пути, коллизии, потенциальное пересечение с существующими маршрутами `ApiService`) — имеет смысл делать последней и явно проговорить план коллизий (3.5) до начала.

## 6. Не входит в этот план (сознательно)

- Templating подстановок (`{{request...}}`, `{{uuid}}`, `{{now}}`) для ответов провайдера — отдельный пункт «Динамика ответов» в `project-brief.md`, пересекается с Фазой D по коду, но не по объёму задачи.
- Матчинг мок-запросов по query/телу (сейчас только метод+путь) — отдельный пробел MVP-скоупа, не обязателен для Типа 3 (валидация тела ≠ матчинг по телу), но стоит держать в голове, если два `MockEndpoint` с одинаковым методом+путём когда-нибудь понадобятся.
- CI/CD-интеграция контракт-тестов — явно вне MVP (`project-brief.md`, «Осознанно не входит в MVP»).
