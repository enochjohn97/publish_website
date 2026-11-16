using System.ComponentModel.DataAnnotations;

namespace Folio.Web.Models;

public sealed class AdminLoginForm
{
    [Required(ErrorMessage = "Enter your username.")]
    [StringLength(50, MinimumLength = 3, ErrorMessage = "Username must be 3–50 characters.")]
    public string Username { get; set; } = "";

    [Required(ErrorMessage = "Enter your password.")]
    [StringLength(200, MinimumLength = 8, ErrorMessage = "Password must be at least 8 characters.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = "";
}

public sealed class StatusUpdateRequest
{
    public string Status { get; set; } = "";
}

public sealed class ModerationRequest
{
    public string Action { get; set; } = "";
}

public sealed class ContributorRequest
{
    public string BookId { get; set; } = "";
    public string UserId { get; set; } = "";
}

public sealed class ProgressRequest
{
    public string ChapterId { get; set; } = "";
    public int Progress { get; set; }
}
