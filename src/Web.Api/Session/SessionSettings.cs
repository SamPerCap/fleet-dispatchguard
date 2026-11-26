namespace DispatchGuard.API.Session;

/// <param name="AllowsPlainHttp">A secure cookie would not travel over the plain HTTP of a development machine.</param>
public sealed record SessionSettings(bool IsRequired, bool AllowsPlainHttp)
{
    public const string TestingEnvironment = "Testing";

    private const string RequiredSetting = "Session:Required";

    /// <exception cref="InvalidOperationException">The session is turned off outside Testing: that would publish an open API.</exception>
    public static SessionSettings From(IConfiguration configuration, IHostEnvironment environment)
    {
        var isTesting = environment.IsEnvironment(TestingEnvironment);
        var isRequired = configuration.GetValue(RequiredSetting, !isTesting);
        if (!isRequired && !isTesting)
        {
            throw new InvalidOperationException($"{RequiredSetting} can only be turned off in the {TestingEnvironment} environment");
        }

        return new SessionSettings(isRequired, isTesting || environment.IsDevelopment());
    }
}
