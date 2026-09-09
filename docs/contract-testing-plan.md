# Contract Testing — план реализации

**Статус:** Фаза A реализована; B–E — не начаты.
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
- **`CallRecord`/`CallDirection`** — сущность для истории вызовов. `CallDirection` уже включает `InboundHttpRequest`, `OutboundBrokerPublish`, `OutboundHttpRequest`. **Но:** `ICallRecordRepository` сейчас **write-only** (`InsertAsync` — и всё, ни списка, ни чтения) и **`InboundHttpRequest` никем не пишется** — только `RunTestScenarioHandler` пишет `OutboundHttpRequest`/`OutboundBrokerPublish`.

### 2.2 Тип 1 (Consumer, request/response) — реализовано частично

`TestScenario` + `RunTestScenarioHandler` + `MessageSender` (HTTP-ветка) уже делают «отправить запрос операции через Http-connection, получить статус+тело». **Чего нет:** проверки тела ответа против схемы операции — `RunTestScenarioResult`/`CallRecord` содержат сырой ответ, но `Success` означает только «получили HTTP-ответ», не «ответ соответствует спеке». Схема ответа вообще нигде не сохраняется (см. 2.5).

### 2.3 Тип 2 (Producer, fire-and-forget) — реализовано полностью

`TestScenario` + `MessageSender` (RabbitMq/Nats-ветка): `ConnectionFactory.CreateConnectionAsync` + `BasicPublishAsync` для RabbitMQ, `NatsConnection.PublishAsync` для NATS. Ответа не ждём — это соответствует описанию «отправляем и всё». Валидация тут по определению не нужна (fire-and-forget), так что дополнительной работы не требуется.

### 2.4 Тип 3 (Provider, request/response) — реализовано частично, с существенным пробелом

`MockInvocationEndpoints` (`POST/GET/... /mock/{**path}`) + `OperationKeyMatcher` + `InvokeMockEndpointHandler` — matching по методу и пути (без query/body), отдают `ExampleTemplate`. Три пробела относительно того, что описал заказчик:

1. **Путь.** Мок сейчас живёт под префиксом `/mock/...`, а не на «именно том пути, который указан в спецификации» — реальный сервис, который настроен стучаться на `/orders`, никогда не попадёт на `/mock/orders` сам по себе. Нужен режим раздачи на реальном пути (см. 4.3 — там же риски).
2. **Валидация входящего запроса.** Сейчас `InvokeMockEndpointHandler` вообще не смотрит на тело запроса — только матчит операцию и отдаёт статичный пример. Нет ни схемы, ни валидатора.
3. **История.** `CallDirection.InboundHttpRequest` существует, но `MockInvocationEndpoints`/`InvokeMockEndpointHandler` не пишут `CallRecord` вообще — обычные mock-вызовы сейчас нигде не логируются, не то что провалидированные.

### 2.5 Тип 4 (Provider, async listen) — не реализовано

Нет ни абстракции слушателя, ни подписки на RabbitMQ/NATS, ни соответствующего вида `TestScenario`. `MessageSender` умеет только публиковать.

### 2.6 Сквозной пробел: схемы операций нигде не хранятся

`OpenApiSpecificationParser`/`AsyncApiSpecificationParser` при парсинге видят полную JSON Schema (через `OpenApiSchema`/через ручной YAML-обход), но `ParsedOperation`/`MockEndpoint` сохраняют только `ExampleJson`/`ExampleTemplate` — саму схему выбрасывают. Без неё невалидно ни Тип 1 (ответ), ни Тип 3 (запрос), ни Тип 4 (payload). Это первое, что нужно закрыть — см. Фазу A ниже.

## 3. Открытые решения (нужно подтвердить до реализации)

1. ~~**Библиотека JSON Schema.**~~ **Решено и сделано (Фаза A):** [`JsonSchema.Net`](https://github.com/gregsdennis/json-everything) 7.3.4, пинована в `Directory.Packages.props`, используется только внутри `SchemaValidator` (`VroksNet.Infrastructure.SchemaValidation`) — наружу течёт исключительно через `ISchemaValidator`.
2. ~~**Где хранить схему.**~~ **Решено и сделано (Фаза A):** на импорте — `MockEndpoint.RequestSchema`/`ResponseSchema` (nullable `string`, миграция `AddMockEndpointSchemas`), заполняются `ImportOpenApiSpecHandler`/`ImportAsyncApiSpecHandler` из `ParsedOperation.RequestSchemaJson`/`ResponseSchemaJson`. Для AsyncAPI `ResponseSchemaJson` несёт схему payload'а сообщения (решение по пункту ниже про "одно поле вместо трёх" — см. 4.1).
3. **Модель данных для «Слушать» (Тип 4).** Расширить существующий `TestScenario` полем `Kind` (`SendHttp` / `PublishBroker` / `ListenBroker`) с опциональным `TimeoutSeconds`, или завести отдельную сущность. **Рекомендация: расширить `TestScenario`** — три «по требованию»-режима укладываются в одну модель «операция + connection + как именно её прогнать», отличие только в поведении при Run.
4. **Модель для пассивного Provider-режима (Тип 3).** Это НЕ «сценарий, который прогоняют» — это состояние мок-эндпоинта. Предлагается булев флаг на `MockEndpoint` (например `ServeAtRealPath` + `ValidateIncomingRequests`, можно объединить в один) в дополнение к существующему `IsEnabled`, а не отдельная сущность.
5. **Коллизии реальных путей.** Если два импортированных спеки объявляют одинаковый путь (`/health` у обоих), и оба включат «раздавать на реальном пути» — конфликт. Нужна политика: либо запрет на включение флага при коллизии (проверка при выставлении флага), либо явный приоритет (например, последний включённый побеждает — рискованно). Отдельный риск: реальный путь может случайно перекрыть существующий маршрут `ApiService` (`/api/...`, `/health`, `/`) — нужен guard-список зарезервированных префиксов.
6. **История/отчётность.** Без read-стороны у `ICallRecordRepository` результаты Типа 3 (и отчасти Типа 4, если сохранять историю прогонов) физически некуда посмотреть, кроме как через сам ответ одного Run. Нужна страница «История вызовов» (уже заявлена в MVP, раздел 2 `project-brief.md`) — реализация read-стороны репозитория и минимального списка/фильтра.

## 4. Целевая архитектура

### 4.1 Фаза A — фундамент: схемы + валидатор (без изменения поведения) — ✅ реализовано

- `ParsedOperation` (`ISpecificationParser`) расширена схемами (оба новых параметра — с дефолтом `null`, чтобы не ломать существующие места создания):
  ```
  ParsedOperation(string OperationKey, string? ExampleJson, string? RequestSchemaJson = null, string? ResponseSchemaJson = null)
  ```
  `OpenApiSpecificationParser` берёт `RequestSchemaJson` из `requestBody.content["application/json"].schema`, `ResponseSchemaJson` — из схемы первого JSON-ответа (тот же response, из которого берётся example) — оба сериализуются через `IOpenApiSchema.SerializeAsV31(...)` с `OpenApiWriterSettings { InlineLocalReferences = true }`, так что локальные `$ref` на `#/components/schemas/...` разворачиваются в текст самой схемы (иначе схема невалидна сама по себе вне контекста всего документа). `AsyncApiSpecificationParser` кладёt payload-схему сообщения в `ResponseSchemaJson` (решение принято — не заводить третье поле под HTTP-нейтральный термин), тем же способом резолвя один `$ref`-хоп на `components.schemas.*` вручную (как уже делалось для example).
- `MockEndpoint` получил `RequestSchema`/`ResponseSchema` (nullable `string`) — миграция `20260909105919_AddMockEndpointSchemas`. `ImportOpenApiSpecHandler`/`ImportAsyncApiSpecHandler` копируют их из `ParsedOperation`. **Пока нигде не отдаются наружу** (не в `MockEndpointDetail`/API/UI) — это по плану, поведение не меняется до Фазы B.
- `ISchemaValidator`/`SchemaValidationResult` в `Application/Abstractions`; реализация — `VroksNet.Infrastructure.SchemaValidation.SchemaValidator` на `JsonSchema.Net` (`EvaluationOptions { OutputFormat = OutputFormat.List }`, чтобы получить плоский список сообщений об ошибках, а не только «не прошло»).
- Тесты: `OpenApiSpecificationParserTests`/`AsyncApiSpecificationParserTests` дополнены проверками, что схема извлекается и `$ref` действительно разворачивается (`Assert.DoesNotContain("$ref", ...)`); новый `SchemaValidatorTests` — чистые юнит-тесты валидатора (валидный/невалидный instance, неправильный тип, нарушение `minimum`, битый JSON) — без Docker, как и остальная такая инфраструктура (`ConnectionTesterTests`/`MessageSenderTests`).

### 4.2 Фаза B — Тип 1: валидация ответа при Send

- `RunTestScenarioHandler` (или отдельный шаг/pipeline behavior) после `IMessageSender.SendAsync` для `Http`-connection вызывает `ISchemaValidator.Validate(endpoint.ResponseSchemaJson, result.ResponseBody)`, если схема есть.
- `RunTestScenarioResult`/`MessageSendResult` получают поле под результат валидации (например `SchemaValidationResult? Validation`) — HTTP-успех и контрактное соответствие теперь разные вещи: запрос может вернуть 200, но с телом не по схеме.
- `CallRecord` получает поле(я) под исход валидации (см. 3.6 — общее для Типов 1/3/4).
- UI (Test Scenarios): бейдж успеха/неуспеха уже есть под HTTP-статус; добавляется отдельный бейдж/список ошибок под контрактную валидацию.

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
- `TestScenario.Kind = ListenBroker` (см. 3.3) + `TimeoutSeconds`. `RunTestScenarioHandler` ветвится по `Kind`: `SendHttp`/`PublishBroker` — как сейчас, `ListenBroker` — вызывает `IMessageListener`, затем (если получено) `ISchemaValidator` по `PayloadSchemaJson` операции.
- Новое значение `CallDirection.InboundBrokerMessage` (симметрично `OutboundBrokerPublish`).
- UI: при выборе AsyncAPI-операции с `action: receive` — показывать режим «Listen», поле таймаута; кнопка Run показывает «Ожидание…» на время таймаута.

### 4.4 Фаза D — Тип 3: Provider-режим (реальный путь + валидация входящих)

Самая рискованная фаза — требует решения по 3.5 до старта.

- Флаг(и) на `MockEndpoint` (см. 3.4), редактируемые из UI (карточка операции на странице спецификации).
- Новый роутинг-слой в `ApiService`, параллельный `MockInvocationEndpoints`: catch-all на корневых путях включённых операций (не под `/mock`). Нужна проверка коллизий при включении флага (см. 3.5) — вероятно, отдельный Application-хендлер `EnableProviderMode(MockEndpointId)`, который проверяет пересечения путей по всем включённым `MockEndpoint` перед тем, как позволить включить.
- `InvokeMockEndpointHandler` (или его аналог для этого роута) валидирует тело входящего запроса против `RequestSchemaJson`, пишет `CallRecord` (`Direction = InboundHttpRequest`, ошибки валидации) — независимо от результата валидации, ответ всё равно отдаётся по текущей логике (пример / будущий templating из раздела «Динамика ответов» в `project-brief.md`, который тоже пока не реализован — отдельная, не блокирующая эту фазу задача).

### 4.5 Фаза E — История вызовов (нужна не позже Фазы D, стоит сделать раньше)

- Read-сторона `ICallRecordRepository` (список с пагинацией/фильтром по спеке/операции/направлению).
- Страница «Call History» в `VroksNet.Web` — без неё результаты Типа 3 (и отчасти Типа 1/4, если смотреть не только последний Run) физически негде увидеть. Технически не зависит от Фаз B–D, можно сделать сразу после Фазы A на существующих `OutboundHttpRequest`/`OutboundBrokerPublish` записях (которые `RunTestScenarioHandler` уже пишет) — и того будет достаточно, чтобы отдать пользе раньше, не дожидаясь Provider-режима.

## 5. Порядок реализации (рекомендация)

1. ✅ **Фаза A** (схемы + валидатор) — сделано, см. 4.1.
2. **Фаза E** (история) — следующий шаг. Можно почти сразу, отдача видна сразу на уже существующих Send/Publish-записях.
3. **Фаза B** (Тип 1) — наименьший риск, прямое расширение уже существующего `TestScenario.Run`.
4. **Фаза C** (Тип 4) — новый, но изолированный компонент (`IMessageListener`), не трогает существующие роуты.
5. **Фаза D** (Тип 3) — самая рискованная (реальные пути, коллизии, потенциальное пересечение с существующими маршрутами `ApiService`) — имеет смысл делать последней и явно проговорить план коллизий (3.5) до начала.

## 6. Не входит в этот план (сознательно)

- Templating подстановок (`{{request...}}`, `{{uuid}}`, `{{now}}`) для ответов провайдера — отдельный пункт «Динамика ответов» в `project-brief.md`, пересекается с Фазой D по коду, но не по объёму задачи.
- Матчинг мок-запросов по query/телу (сейчас только метод+путь) — отдельный пробел MVP-скоупа, не обязателен для Типа 3 (валидация тела ≠ матчинг по телу), но стоит держать в голове, если два `MockEndpoint` с одинаковым методом+путём когда-нибудь понадобятся.
- CI/CD-интеграция контракт-тестов — явно вне MVP (`project-brief.md`, «Осознанно не входит в MVP»).
