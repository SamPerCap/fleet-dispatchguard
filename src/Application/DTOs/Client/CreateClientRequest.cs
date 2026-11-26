using System.ComponentModel.DataAnnotations;
using ClientEntity = DispatchGuard.Core.Entities.Client;

namespace DispatchGuard.Application.DTOs.Client;

public class CreateClientRequest
{
    [Required(ErrorMessageResourceType = typeof(AnnotationTexts), ErrorMessageResourceName = nameof(AnnotationTexts.NameRequired))]
    [StringLength(ClientEntity.MaxNameLength, ErrorMessageResourceType = typeof(AnnotationTexts), ErrorMessageResourceName = nameof(AnnotationTexts.NameLength))]
    public string Name { get; set; } = string.Empty;

    [StringLength(50, ErrorMessageResourceType = typeof(AnnotationTexts), ErrorMessageResourceName = nameof(AnnotationTexts.TaxIdLength))]
    public string? TaxId { get; set => field = OptionalText.NullIfBlank(value); }

    [EmailAddress(ErrorMessageResourceType = typeof(AnnotationTexts), ErrorMessageResourceName = nameof(AnnotationTexts.InvalidEmail))]
    [StringLength(100, ErrorMessageResourceType = typeof(AnnotationTexts), ErrorMessageResourceName = nameof(AnnotationTexts.EmailLength))]
    public string? ContactEmail { get; set => field = OptionalText.NullIfBlank(value); }

    [Phone(ErrorMessageResourceType = typeof(AnnotationTexts), ErrorMessageResourceName = nameof(AnnotationTexts.InvalidPhone))]
    [StringLength(20, ErrorMessageResourceType = typeof(AnnotationTexts), ErrorMessageResourceName = nameof(AnnotationTexts.PhoneLength))]
    public string? ContactPhone { get; set => field = OptionalText.NullIfBlank(value); }

    public bool IsExternal { get; set; }
}