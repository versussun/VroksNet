# Contract Testing — план реализации

**Статус:** Фазы A, B и E реализованы; C и D — не начаты.
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

### 2.5 Тип 4 (Provider, async listen) — не реализовано

Нет ни абстракции слушателя, ни подписки на RabbitMQ/NATS, ни соответствующего вида `TestScenario`. `MessageSender` умеет только публиковать.

### 2.6 Схемы операций — ✅ закрыто Фазой A

Раньше парсеры видели полную JSON Schema, но `ParsedOperation`/`MockEndpoint` сохраняли только `ExampleJson`/`ExampleTemplate`, а схему выбрасывали — без неё не валидировался ни один из Типов 1/3/4. Теперь схемы извлекаются и хранятся в `MockEndpoint.RequestSchema`/`ResponseSchema` (для AsyncAPI payload-схема лежит в `ResponseSchema`), есть `ISchemaValidator`. Подробности — в 4.1. Тип 1 их уже использует (Фаза B, по схемам всех статусов — см. 4.2); Типы 3/4 — Фазы C–D.

## 3. Открытые решения (нужно подтвердить до реализации)

1. ~~**Библиотека JSON Schema.**~~ **Решено и сделано (Фаза A):** [`JsonSchema.Net`](https://github.com/gregsdennis/json-everything) 7.3.4, пинована в `Directory.Packages.props`, используется только внутри `SchemaValidator` (`VroksNet.Infrastructure.SchemaValidation`) — наружу течёт исключительно через `ISchemaValidator`.
2. ~~**Где хранить схему.**~~ **Решено и сделано (Фаза A):** на импорте — `MockEndpoint.RequestSchema`/`ResponseSchema` (nullable `string`, миграция `AddMockEndpointSchemas`), заполняются `ImportOpenApiSpecHandler`/`ImportAsyncApiSpecHandler` из `ParsedOperation.RequestSchemaJson`/`ResponseSchemaJson`. Для AsyncAPI `ResponseSchemaJson` несёт схему payload'а сообщения (решение по пункту ниже про "одно поле вместо трёх" — см. 4.1).
3. **Модель данных для «Слушать» (Тип 4).** Расширить существующий `TestScenario` полем `Kind` (`SendHttp` / `PublishBroker` / `ListenBroker`) с опциональным `TimeoutSeconds`, или завести отдельную сущность. **Рекомендация: расширить `TestScenario`** — три «по требованию»-режима укладываются в одну модель «операция + connection + как именно её прогнать», отличие только в поведении при Run.
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

### 4.3 Фаза C — Тип 4: Listen & validate

- Новая абстракция, симметричная `IMessageSender`:
  ```
  public interface IMessageListener
  {
      Task<MessageListenResult> ListenAsync(Connection connection, string operationKey, TimeSpan timeout, CancellationToken cancellationToken);
  }
  public sealed record MessageListenResult(bool Received, string? Payload, string? Message);
  ```
- `MessageListener` (Infrastructure) — RabbitMQ: временная эксклюзивная очередь, `QueueBindAsync` на нужный routing key (адрес канала), `BasicConsumeAsync`/ожидание с таймаутом, удаление очереди по завершении. NATS: `NatsConnection.SubscribeAsync(subject)` с таймаутом на первое сообщение. Оба — короткоживущие, без постоянной подписки (в отличие от Типа 3, который живёт пока включён).
- `TestScenario.Kind = ListenBroker` (см. 3.3) + `TimeoutSeconds`. `RunTestScenarioHandler` ветвится по `Kind`: `SendHttp`/`PublishBroker` — как сейчас, `ListenBroker` — вызывает `IMessageListener`, затем (если получено) `ISchemaValidator` по `MockEndpoint.ResponseSchema` операции (для AsyncAPI это payload-схема — отдельного поля нет, см. 3.2).
- Новое значение `CallDirection.InboundBrokerMessage` (симметрично `OutboundBrokerPublish`).
- UI: при выборе AsyncAPI-операции с `action: receive` — показывать режим «Listen», поле таймаута; кнопка Run показывает «Ожидание…» на время таймаута.

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
4. **Фаза C** (Тип 4) — новый, но изолированный компонент (`IMessageListener`), не трогает существующие роуты. Перед стартом — закрыть 3.3 (`TestScenario.Kind`).
5. **Фаза D** (Тип 3) — самая рискованная (реальные пути, коллизии, потенциальное пересечение с существующими маршрутами `ApiService`) — имеет смысл делать последней и явно проговорить план коллизий (3.5) до начала.

## 6. Не входит в этот план (сознательно)

- Templating подстановок (`{{request...}}`, `{{uuid}}`, `{{now}}`) для ответов провайдера — отдельный пункт «Динамика ответов» в `project-brief.md`, пересекается с Фазой D по коду, но не по объёму задачи.
- Матчинг мок-запросов по query/телу (сейчас только метод+путь) — отдельный пробел MVP-скоупа, не обязателен для Типа 3 (валидация тела ≠ матчинг по телу), но стоит держать в голове, если два `MockEndpoint` с одинаковым методом+путём когда-нибудь понадобятся.
- CI/CD-интеграция контракт-тестов — явно вне MVP (`project-brief.md`, «Осознанно не входит в MVP»).
