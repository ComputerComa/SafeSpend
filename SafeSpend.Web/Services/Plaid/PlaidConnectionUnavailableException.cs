namespace SafeSpend.Web.Services.Plaid;

public sealed class PlaidConnectionUnavailableException(
    string message,
    Exception innerException) : Exception(message, innerException);
