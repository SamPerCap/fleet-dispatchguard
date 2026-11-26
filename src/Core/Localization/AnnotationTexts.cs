namespace DispatchGuard.Core.Localization;

/// <summary>For the annotations of the DTOs, which look for a static property by its name (<c>ErrorMessageResourceName</c>).</summary>
public static class AnnotationTexts
{
    public static string NameRequired => Texts.NameRequiredAnnotation().In(CurrentLanguage.Value);

    public static string NameLength => Texts.NameLengthAnnotation().In(CurrentLanguage.Value);

    public static string TaxIdLength => Texts.TaxIdLengthAnnotation().In(CurrentLanguage.Value);

    public static string InvalidEmail => Texts.InvalidEmail().In(CurrentLanguage.Value);

    public static string EmailLength => Texts.EmailLengthAnnotation().In(CurrentLanguage.Value);

    public static string InvalidPhone => Texts.InvalidPhone().In(CurrentLanguage.Value);

    public static string PhoneLength => Texts.PhoneLengthAnnotation().In(CurrentLanguage.Value);

    public static string ReasonLength => Texts.ReasonLengthAnnotation().In(CurrentLanguage.Value);

    public static string InvalidVisitName => Texts.InvalidVisitName().In(CurrentLanguage.Value);
}
