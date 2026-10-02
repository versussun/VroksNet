# VroksNet — Project Brief

**Status:** Draft v0.8
**Stack:** .NET 10, .NET Aspire, Clean Architecture, Blazor WebAssembly, Docker
**Users:** single internal team, no auth on MVP

Внутренний сервер для мокинга API и contract testing по спецификациям OpenAPI и AsyncAPI — аналог Microcks на стеке .NET.

> Правила структуры кода и стиля — в `.claude/CLAUDE.md`. Этот документ — про продукт: зачем, что входит в MVP, архитектура и план.

## 1. Цель

Команде нужен инструмент для двух связанных задач: (1) мокинг API — чтобы фронтенд и смежные сервисы могли разрабатываться и тестироваться независимо от готовности реального бэкенда, и (2) contract testing — чтобы проверять, что реальные сервисы действительно соответствуют заявленным спецификациям.

В отличие от Microcks, охват сознательно узкий: без мультитенантности, без встроенной аутентификации, без интеграции с CI/CD на первом этапе. Сервис разворачивается через .NET Aspire (в разработке и как оркестратор контейнеров) и одним docker-набором в продакшене внутри инфраструктуры команды.

## 2. Объём MVP

По обеим спецификациям сразу, без деления на «REST сначала, async потом».

| Область | Что делает |
|---|---|
| Загрузка спецификаций | Импорт OpenAPI (2.0/3.x, YAML/JSON) и AsyncAPI (2.x/3.x) файлов через UI и через REST API |
| REST-моки | Генерация ответов по примерам из спецификации (examples / schema-based fallback), матчинг по методу, пути, query и телу запроса |
| Async-моки | Публикация тестовых сообщений в NATS, RabbitMQ и Kafka по описанным в AsyncAPI каналам, с заданной периодичностью или по триггеру |
| Управление моками | Blazor-панель: список загруженных спецификаций, включение/выключение мок-эндпоинтов, редактирование примеров |
| История вызовов | Лог входящих запросов к REST-мокам и отправленных async-сообщений — для отладки |
| Contract testing | Разовая проверка «схема ⇄ реальный ответ» вручную из UI (без встраивания в пайплайн) |

### Динамика ответов

Решено: простой **templating**, без скриптов и без сценарного переключения ответов.

Плейсхолдеры в теле/заголовках ответа подставляются значениями из запроса на момент матчинга:

| Плейсхолдер | Источник |
|---|---|
| `{{request.path.<name>}}` | Значение path-параметра из URL |
| `{{request.query.<name>}}` | Значение query-параметра |
| `{{request.header.<name>}}` | Заголовок запроса |
| `{{request.body.<jsonpath>}}` | Значение из тела запроса (JSON path) |
| `{{uuid}}` | Сгенерированный GUID — для полей вроде `id` в ответе |
| `{{now}}` | Текущая метка времени (ISO 8601) |

Работает поверх examples из спецификации: движок берёт example как шаблон и подставляет плейсхолдеры перед отправкой ответа. Без переменных — просто статичный example, как раньше.

**Сознательно не сейчас:** переключение мока на конкретный сценарий (например, заголовок `X-Mock-Scenario: not-found` → 404 вместо happy path). Это отдельная фича следующей итерации — на старте достаточно happy path со статическими/шаблонизированными examples из спеки.

### Версионирование спецификаций

Решено: при повторной загрузке спецификация **всегда заменяет** предыдущую версию — истории версий нет, активна только последняя загрузка.

Сопоставление «это обновление существующей спеки, а не новая» — по **имени/title внутри самой спеки** (`info.title` в OpenAPI и AsyncAPI), а не по имени файла: файл можно переименовать, а `info.title` — смысловой идентификатор сервиса, как это делает Microcks.

Открытый нюанс для реализации (не блокер для MVP, но стоит держать в голове в фазе 01): если пользователь вручную отредактировал примеры мока в Admin UI, а потом загрузил новую версию спеки с тем же title — эти ручные правки будут перезаписаны данными из новой спеки. На старте это ожидаемое поведение; если станет болью — можно будет добавить точечное предупреждение в UI перед заменой.

### Осознанно не входит в MVP

- Аутентификация, роли, мультитенантность
- CI/CD-интеграция (CLI, Maven/Gradle/NuGet-плагины, webhook-триггеры)
- Security testing спецификаций
- MQTT, WebSocket — можно добавить позже, если появится нужда (Kafka добавлена после MVP — см. «Фаза 05»)
- Кластеризация / горизонтальное масштабирование
- Сценарии ошибок (`X-Mock-Scenario` и подобное) — см. «Динамика ответов» выше
- История версий спецификаций — см. «Версионирование» выше

## 3. Архитектура

Solution уже создан по Clean Architecture и оркестрируется .NET Aspire. Продуктовые компоненты брифа ложатся на существующие проекты один в один:

| Компонент брифа | Проект в solution | Роль |
|---|---|---|
| Оркестрация (dev) | `VroksNet.AppHost` | Aspire AppHost: в разработке поднимает ApiService и Web как отдельные процессы с hot reload, плюс NATS, RabbitMQ, Kafka как ресурсы — без ручного docker-compose |
| Сквозная инфраструктура | `VroksNet.ServiceDefaults` | Телеметрия, health checks, resilience — общие для ApiService и Web |
| Mock API + хост Admin UI | `VroksNet.ApiService` | Composition root: динамический роутинг REST-запросов по OpenAPI-спекам, регистрирует Mediator. В Production дополнительно сервит собранные статические файлы `VroksNet.Web` (wwwroot + SPA-фолбэк на `index.html`) — единственный процесс в контейнере |
| Admin UI | `VroksNet.Web` | Blazor **WebAssembly** (standalone, браузерный проект): загрузка спек, управление моками, просмотр истории вызовов. В Production не запускается как отдельный процесс — только собирается, а раздаёт его `VroksNet.ApiService` |
| Spec Engine | `VroksNet.Application` | Парсинг/валидация OpenAPI и AsyncAPI, генерация примеров, матчинг запросов — как use cases через [martinothamar/Mediator](https://github.com/martinothamar/Mediator) |
| Домен | `VroksNet.Domain` | Спецификации, мок-эндпоинты, каналы, записи истории вызовов как сущности/value objects — без ссылок на фреймворки |
| Async Worker + Storage | `VroksNet.Infrastructure` | `BackgroundService` для публикации в NATS/RabbitMQ/Kafka по каналам из AsyncAPI; EF Core-реализации репозиториев из Application |

Зависимости идут внутрь, как описано в `.claude/CLAUDE.md`: `Domain ← Application ← Infrastructure`, `Presentation → Application`.

### Деплой: один процесс, один образ

**Исходная точка (историческая справка):** проект начинался со стандартного Aspire Starter-шаблона, где `VroksNet.Web` был Blazor **Server** — собственный Kestrel-процесс и отдельный образ при публикации. Автоматического «слияния в один образ по `ASPNETCORE_ENVIRONMENT=Production`» в такой схеме нет.

**Решение (✅ реализовано):** перевести `VroksNet.Web` на Blazor **WebAssembly** (standalone) и раздавать его как статику из `VroksNet.ApiService` в Production — классический паттерн «backend отдаёт SPA».

Что это значит технически:
- `VroksNet.Web.csproj` меняет SDK на `Microsoft.NET.Sdk.BlazorWebAssembly` (standalone-клиент, без собственного Kestrel/server-side рендеринга).
- `VroksNet.ApiService` в Production раздаёт собранные wasm-файлы как статику и делает SPA-фолбэк на `index.html` для незнакомых маршрутов (`app.UseStaticFiles()` / `app.MapFallbackToFile("index.html")`; `UseBlazorFrameworkFiles()` в .NET 8+ не нужен).
- Сборка: multi-stage Dockerfile — сначала `dotnet publish` для `VroksNet.Web`, его `wwwroot`/wasm-выход копируется в `wwwroot` `VroksNet.ApiService`, затем публикуется сам `VroksNet.ApiService`. Один Dockerfile, один финальный образ, один процесс в контейнере.
- В разработке `VroksNet.Web` и `VroksNet.ApiService` по-прежнему можно гонять как два процесса через `AppHost` (это даёт hot reload и удобный dev-опыт) — потребуется настроить CORS на `ApiService`, чтобы WASM-клиент на своём dev-порту мог обращаться к API. Это нормально: расхождение dev/prod-топологии тут ожидаемо и не противоречит цели «один образ» — она про то, что деплоится и запускается в проде.
- `.claude/CLAUDE.md` нужно поправить под эту схему: `VroksNet.Web` — WASM-клиент, не отдельный host; `VroksNet.ApiService` — единственный сервер, отдающий и API, и статику UI.

### Хранилище: решение

Выбор сделан: **SQLite**. Ноль внешних зависимостей, один файл внутри контейнера (или на смонтированном volume, чтобы данные переживали пересоздание контейнера), не нужен отдельный Aspire-ресурс для БД — вписывается в идею единого образа из раздела выше.

Ранее отмеченный риск конкурентной записи (async-воркер и Mock API пишут в базу параллельно — история вызовов, статусы) остаётся в силе и требует нескольких обязательных мер на фазе 00, а не «просто EF Core + SQLite по умолчанию»:

- Включить **WAL journal mode** (`PRAGMA journal_mode=WAL`) — позволяет читать во время записи и заметно снижает `SQLITE_BUSY`.
- Задать разумный `busy_timeout` (например, 5 секунд) на уровне подключения, чтобы конкурентные писатели ждали друг друга вместо немедленной ошибки.
- Рассмотреть **сериализацию записи через один канал** (например, `System.Threading.Channels` с одним writer-воркером, который единолично пишет в БД, а Mock API и async-воркер только публикуют в канал) — снимает гонки на уровне приложения, не полагаясь только на блокировки SQLite.
- Мигрировать через EF Core-миграции с самого начала, даже для SQLite — облегчит переход на PostgreSQL позже, если нагрузка вырастет за пределы одной команды.

Если конкурентная запись всё же станет узким местом — миграция на PostgreSQL остаётся доступной через EF Core без переписывания доменного слоя.

## 4. Технический стек

| Слой | Технология | Зачем |
|---|---|---|
| Платформа | .NET 10 | Актуальный LTS-цикл, уже выбран в solution |
| Оркестрация (dev) | .NET Aspire (`AppHost`, `ServiceDefaults`) | Запуск ApiService + Web + NATS + RabbitMQ + Kafka в dev с hot reload; в Production не используется — деплоится один образ |
| Use-case dispatch | martinothamar/Mediator (source-generator, без рефлексии) | Уже принят как стандарт в `.claude/CLAUDE.md` — не MediatR |
| Backend / Mock API | ASP.NET Core (`VroksNet.ApiService`) | Composition root; в Production также раздаёт статику Admin UI |
| Admin UI | Blazor WebAssembly, standalone (`VroksNet.Web`) | Браузерный SPA-клиент; в Production собирается и раздаётся как статика из `ApiService`, отдельно не запускается |
| OpenAPI-парсинг | Microsoft.OpenApi.Readers | Официальная .NET библиотека, поддержка 2.0/3.x |
| AsyncAPI-парсинг | Кастомный парсер над YamlDotNet | Готовых зрелых .NET-библиотек для AsyncAPI 3.x пока нет — парсим JSON-схему спецификации сами |
| Async-брокеры | NATS.Client, RabbitMQ.Client, Confluent.Kafka | Официальные .NET-клиенты под выбранные протоколы |
| Хранилище | SQLite + EF Core (WAL mode, сериализованная запись) | См. раздел 3 |
| Деплой | Docker, multi-stage build (Web → статика → ApiService) | Один Dockerfile, один образ, один процесс в проде |

## 5. Дорожная карта

Пять фаз, каждая — рабочий инкремент, который можно показать команде.

### Фаза 00 — Каркас проекта ✅ готово
Solution с Clean Architecture и Aspire (`VroksNet.slnx`, все проекты в `src/`, `.claude/CLAUDE.md` с правилами); SQLite + EF Core с WAL, busy timeout и сериализованной записью через один канал (`DbWriteQueue`/`DbWriteBackgroundService`); NATS и RabbitMQ — Aspire-ресурсы в `AppHost`.
**Результат:** `dotnet run --project VroksNet.AppHost` поднимает весь стек, включая инфраструктуру

### Фаза 01 — OpenAPI → REST-моки ✅ готово
Импорт OpenAPI с replace-по-title, динамический роутинг в `/mock`, шаблонизированные ответы со статусом из спеки (сделано как Фаза F `docs/contract-testing-plan.md`, 4.6). Генерации ответа по схеме для операций без example нет — отдаётся `{}`.

Загрузка и парсинг OpenAPI в `VroksNet.Application` (сопоставление по `info.title`, замена при повторной загрузке), генерация ответов по examples/схеме с подстановкой плейсхолдеров (`{{request...}}`, `{{uuid}}`, `{{now}}`), динамический роутинг запросов в `VroksNet.ApiService`.
**Результат:** загрузили спеку — получили рабочий REST-мок по её эндпоинтам, с шаблонизированными ответами

### Фаза 02 — Admin UI на Blazor WebAssembly ✅ готово
Список спецификаций, карточка мока, включение/выключение эндпоинтов, простой просмотр истории вызовов в `VroksNet.Web` (WASM); публикация как статики из `VroksNet.ApiService`, multi-stage Dockerfile для единого образа.
**Результат:** моками можно управлять без прямых обращений к API, приложение деплоится одним образом

### Фаза 03 — AsyncAPI → NATS / RabbitMQ ✅ готово
Импорт AsyncAPI; публикация и прослушивание через тест-сценарии; **Publishers** — async-моки: сущность `Publisher` (операция + брокерное подключение + exchange + интервал 1 с – 24 ч), `PublisherBackgroundService` раз в секунду публикует всё, чему подошёл срок, плюс «Publish now» из UI. Payload — шаблон (`{{uuid}}`, `{{now}}`), сверяется со схемой сообщения, каждая публикация пишется в историю вызовов. Триггер — расписание и ручной запуск; триггера «по вызову HTTP-мока» нет (решение заказчика).

Парсинг AsyncAPI, `BackgroundService`-воркер публикации сообщений в `VroksNet.Infrastructure`, настройка периодичности/триггеров из UI.
**Результат:** загрузили AsyncAPI-спеку — сервис публикует мок-события в брокер

### Фаза 04 — Contract testing + полировка ✅ готово
**Сделано:** все четыре вида contract-тестов, история вызовов, динамика ответов, exchange для Send и CORS на провайдерском порту (Фазы A–H плана), руководства в `docs/guides/contract-testing/`, документация по эксплуатации — `docs/runbook.md`.

Ручная проверка «схема ⇄ реальный ответ», история и логи, документация по эксплуатации. Детальный план реализации (4 вида тестов, обзор уже сделанного, порядок фаз) — `docs/contract-testing-plan.md`.
**Результат:** инструмент, который можно передать команде и не сопровождать вручную


### Фаза 05 — Kafka ✅ готово
Третий тип брокерного `Connection` — `Kafka`: проверка подключения (запрос метаданных кластера), Send/Listen тест-сценарии и Publishers. Топик = адрес канала AsyncAPI. Значение подключения — список bootstrap-серверов (`host:9092,host2:9092`) или настройки librdkafka `key=value;…` (для SASL/TLS). Listen не использует consumer group: партиции подходящих топиков назначаются вручную с текущего конца, без коммитов, поэтому реальные консьюмеры ничего не замечают. В dev — Aspire-ресурс `kafka`.

## 6. Первые шаги

- [x] Завести git-репозиторий и структуру solution — уже сделано (`VroksNet`, .NET 10 / Aspire, Clean Architecture)
- [x] Перевести `VroksNet.Web` на `Microsoft.NET.Sdk.BlazorWebAssembly` (standalone), настроить CORS на `VroksNet.ApiService` для dev-режима
- [x] Настроить `VroksNet.ApiService` на раздачу статики WASM-сборки (`UseStaticFiles`, `MapFallbackToFile("index.html")`) для Production
- [x] Написать multi-stage Dockerfile: publish `VroksNet.Web` → копия wwwroot в `VroksNet.ApiService` → publish `VroksNet.ApiService`
- [x] Обновить `.claude/CLAUDE.md` под новую схему (Web — WASM-клиент, не отдельный host)
- [x] Подключить SQLite + EF Core: включить WAL mode и busy timeout, спроектировать сериализованную запись через один канал/воркер
- [x] Добавить NATS и RabbitMQ как ресурсы в `VroksNet.AppHost` (через `AddNats`/`AddRabbitMQ`)
- [ ] Собрать 2–3 реальных OpenAPI-спеки и 1–2 AsyncAPI-спеки из ваших сервисов как тестовые данные — пока есть только учебные спеки в `docs/samples/` (OpenAPI 3.0/3.1, Swagger 2.0, AsyncAPI 3.0 для Kafka/NATS/RabbitMQ — см. `docs/samples/README.md`)
- [x] В `VroksNet.Domain` завести сущности `ApiSpecification` (с `Title` как ключом сопоставления версий), `MockEndpoint`, `CallRecord`; в `VroksNet.Application` — первый use case `ImportOpenApiSpec` через Mediator, реализующий replace-по-title
- [x] Прототип парсинга OpenAPI через Microsoft.OpenApi в `ImportOpenApiSpecHandler` — загрузить файл и вывести список эндпоинтов в консоль/лог
- [x] Спроектировать движок подстановки плейсхолдеров (`{{request.path.*}}`, `{{request.query.*}}`, `{{request.header.*}}`, `{{request.body.*}}`, `{{uuid}}`, `{{now}}`) поверх examples из спеки — `ResponseTemplateEngine`, Фаза F (`docs/contract-testing-plan.md`, 4.6)

## 7. Открытые вопросы

Пока нет — оба вопроса с прошлой версии решены (см. «Динамика ответов» и «Версионирование спецификаций» в разделе 2).
