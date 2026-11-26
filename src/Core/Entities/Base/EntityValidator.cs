namespace DispatchGuard.Core.Entities.Base;

public static class EntityValidator
{
    private const int MinPhoneDigits = 7;

    public static void ValidateRequiredString(string? value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidRequestException(Texts.FieldRequired(paramName));
        }
    }

    /// <param name="subject">Begins the message, as in "The name cannot".</param>
    public static void ValidateMaxLength(string? value, int maxLength, LocalizedText subject)
    {
        if (value?.Length > maxLength)
        {
            throw new InvalidRequestException(Texts.TooLong(subject, maxLength));
        }
    }

    public static void ValidateNotNull<T>(T? value, string paramName) where T : class
    {
        if (value == null)
        {
            throw new ArgumentNullException(paramName);
        }
    }

    public static void ValidatePositive(int value, string paramName)
    {
        if (value <= 0)
        {
            throw new InvalidRequestException(Texts.FieldPositive(paramName));
        }
    }

    public static void ValidatePositive(long value, string paramName)
    {
        if (value <= 0)
        {
            throw new InvalidRequestException(Texts.FieldPositive(paramName));
        }
    }

    public static void ValidateEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return;
        }

        try
        {
            var addr = new System.Net.Mail.MailAddress(email);
            if (addr.Address != email)
            {
                throw new InvalidRequestException(Texts.InvalidEmail());
            }
        }
        catch (FormatException)
        {
            throw new InvalidRequestException(Texts.InvalidEmail());
        }
    }

    public static void ValidatePhoneNumber(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return;
        }

        var digitsOnly = System.Text.RegularExpressions.Regex.Replace(phone, @"[^\d+\-\s()]", "");
        if (digitsOnly.Length < MinPhoneDigits)
        {
            throw new InvalidRequestException(Texts.PhoneTooShort(MinPhoneDigits));
        }
    }

    public static void ValidateNotFuture(DateTime date, string paramName)
    {
        if (date > DateTime.UtcNow)
        {
            throw new InvalidRequestException(Texts.FieldInFuture(paramName));
        }
    }

    public static void ValidateFuture(DateTime date, string paramName)
    {
        if (date <= DateTime.UtcNow)
        {
            throw new ArgumentException($"{paramName} must be in the future", paramName);
        }
    }

    public static void ValidateDateOrder(DateTime start, DateTime end, string startParamName, string endParamName)
    {
        if (start >= end)
        {
            throw new ArgumentException($"{endParamName} must be after {startParamName}", endParamName);
        }
    }

    public static void ValidateRange(int value, int min, int max, string paramName)
    {
        if (value < min || value > max)
        {
            throw new ArgumentException($"{paramName} must be between {min} and {max}", paramName);
        }
    }

    public static void ValidateNonNegative(TimeSpan value, string paramName)
    {
        if (value < TimeSpan.Zero)
        {
            throw new ArgumentException($"{paramName} cannot be negative", paramName);
        }
    }
}