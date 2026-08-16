using System;
using System.ComponentModel.DataAnnotations;

namespace IdentityService.Attributes;

/// <summary>
/// Validates that a value is an absolute image URL that is safe to store and hand back to
/// clients. Blank values are treated as "cleared" and always pass, so a user can remove the
/// image by sending an empty string.
/// </summary>
/// <remarks>
/// The file extension is deliberately not checked: image hosts frequently serve PNG/JPEG/WebP/GIF
/// from extensionless or query-string URLs, and the client already falls back to its default
/// background when the URL does not render. Non-web schemes (data, javascript, file, ...) are
/// rejected because they are the ones that turn a stored URL into an attack surface.
/// </remarks>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public class ImageUrlAttribute : ValidationAttribute
{
    public int MaximumLength { get; set; } = 2048;

    public bool RequireHttps { get; set; } = true;

    public override bool IsValid(object value)
    {
        if (value == null)
        {
            return true;
        }

        if (value is not string url)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(url))
        {
            return true;
        }

        url = url.Trim();

        if (url.Length > MaximumLength)
        {
            return false;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host))
        {
            return false;
        }

        return RequireHttps
            ? uri.Scheme == Uri.UriSchemeHttps
            : uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp;
    }

    public override string FormatErrorMessage(string name) =>
        RequireHttps
            ? $"{name} must be an absolute https URL of at most {MaximumLength} characters."
            : $"{name} must be an absolute http or https URL of at most {MaximumLength} characters.";
}
