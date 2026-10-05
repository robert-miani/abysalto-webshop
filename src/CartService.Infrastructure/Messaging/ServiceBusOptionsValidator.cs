namespace CartService.Infrastructure.Messaging;

using CartService.Infrastructure.Outbox;
using Microsoft.Extensions.Options;

/// <summary>
/// The relay needs to know where to publish, so a service with an enabled relay and no broker address does not
/// start. When the relay is disabled, no broker is needed.
/// </summary>
internal sealed class ServiceBusOptionsValidator : IValidateOptions<ServiceBusOptions>
{
    private readonly IOptions<OutboxOptions> _outbox;

    public ServiceBusOptionsValidator(IOptions<OutboxOptions> outbox)
    {
        _outbox = outbox;
    }

    public ValidateOptionsResult Validate(string? name, ServiceBusOptions options)
    {
        if (!_outbox.Value.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        bool hasConnectionString = !string.IsNullOrWhiteSpace(options.ConnectionString);
        bool hasNamespace = !string.IsNullOrWhiteSpace(options.FullyQualifiedNamespace);

        if (hasConnectionString == hasNamespace)
        {
            return ValidateOptionsResult.Fail(
                "Set exactly one of ServiceBus:ConnectionString (local emulator) and ServiceBus:FullyQualifiedNamespace (Azure), "
                + "or turn the relay off with Outbox:Enabled=false.");
        }

        return ValidateOptionsResult.Success;
    }
}
