using System.Text.Json;
using Mediator;
using VroksNet.Application.Abstractions;
using VroksNet.Application.CallRecords;
using VroksNet.Domain.CallRecords;

namespace VroksNet.Application.Publishers.PublishNow;

/// <summary>
/// Renders the payload (the override or the operation's example) as a template — there's no
/// request behind a publish, so only <c>{{uuid}}</c>/<c>{{now}}</c> resolve — publishes it through
/// the connection, and logs a <see cref="CallRecord"/> (<see cref="CallDirection.OutboundBrokerPublish"/>)
/// either way. A deleted specification/operation/connection is reported as a failed publish, not thrown.
/// </summary>
public sealed class PublishNowHandler(
    IPublisherRepository publishers,
    IApiSpecificationRepository specifications,
    IConnectionRepository connections,
    IMessageSender sender,
    IResponseTemplateEngine templateEngine,
    ISchemaValidator schemaValidator,
    ICallRecordRepository callRecords) : IRequestHandler<PublishNow, PublishResult?>
{
    private static readonly TemplateContext NoRequest = new(
        new Dictionary<string, string>(), new Dictionary<string, string>(), new Dictionary<string, string>(), null);

    public async ValueTask<PublishResult?> Handle(PublishNow request, CancellationToken cancellationToken)
    {
        var publisher = await publishers.FindByIdAsync(request.Id, cancellationToken);
        if (publisher is null)
        {
            return null;
        }

        // The schedule counts from when the publish started, so a slow broker doesn't stretch the interval.
        var startedAt = DateTimeOffset.UtcNow;

        var specification = await specifications.FindByIdAsync(publisher.SpecificationId, cancellationToken);
        var endpoint = specification?.Endpoints.FirstOrDefault(e => e.Id == publisher.MockEndpointId);
        var connection = await connections.FindByIdAsync(publisher.ConnectionId, cancellationToken);
        if (endpoint is null || connection is null)
        {
            const string missingMessage = "The publisher's specification, operation, or connection no longer exists.";
            await publishers.RecordPublishAsync(publisher.Id, startedAt, success: false, missingMessage, cancellationToken);
            return new PublishResult(false, missingMessage, null, null);
        }

        var template = publisher.PayloadOverride ?? endpoint.ExampleTemplate;
        var rendered = template is null ? null : templateEngine.Render(template, NoRequest);
        var payload = rendered?.Text;

        var result = await sender.SendAsync(connection, endpoint.OperationKey, payload, publisher.Exchange, cancellationToken);

        var validation = result.Success && endpoint.ResponseSchema is { } schema && !string.IsNullOrWhiteSpace(payload)
            ? schemaValidator.Validate(schema, payload)
            : null;
        var message = validation is { IsValid: false }
            ? $"{result.Message} — the message doesn't match the spec ({validation.Errors.Count} violation(s))."
            : result.Message;

        await callRecords.InsertAsync(new CallRecord
        {
            Id = Guid.NewGuid(),
            SpecificationId = publisher.SpecificationId,
            MockEndpointId = publisher.MockEndpointId,
            ConnectionId = connection.Id,
            PublisherId = publisher.Id,
            Direction = CallDirection.OutboundBrokerPublish,
            Timestamp = DateTimeOffset.UtcNow,
            RequestSnapshot = CallRecordSnapshot.Truncate(payload),
            ResponseSnapshot = CallRecordSnapshot.Truncate(result.Message),
            ContractValid = validation?.IsValid,
            ValidationErrors = validation is { IsValid: false } ? JsonSerializer.Serialize(validation.Errors) : null,
            Warnings = rendered is { Warnings.Count: > 0 } ? JsonSerializer.Serialize(rendered.Warnings) : null
        }, cancellationToken);

        await publishers.RecordPublishAsync(publisher.Id, startedAt, result.Success, message, cancellationToken);

        return new PublishResult(result.Success, message, payload, validation);
    }
}
