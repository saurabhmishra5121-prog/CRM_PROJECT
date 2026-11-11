using System.ComponentModel.DataAnnotations;

namespace AcxiomCRM.ViewModels;

public class LeadViewModel
{
    public int LeadId { get; set; }

    [Required(ErrorMessage = "Lead Name is required.")]
    [StringLength(100, ErrorMessage = "Name cannot exceed 100 characters.")]
    [Display(Name = "Lead Name")]
    public string LeadName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Email is required.")]
    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    [StringLength(150, ErrorMessage = "Email cannot exceed 150 characters.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Phone is required.")]
    [RegularExpression(@"^[6-9]\d{9}$", ErrorMessage = "Enter a valid 10-digit mobile number.")]
    public string Phone { get; set; } = string.Empty;

    [StringLength(100, ErrorMessage = "Company name cannot exceed 100 characters.")]
    [Display(Name = "Company Name")]
    public string? CompanyName { get; set; }

    [Required(ErrorMessage = "Source is required.")]
    [StringLength(50)]
    public string Source { get; set; } = "Website";

    [Required(ErrorMessage = "Status is required.")]
    public string Status { get; set; } = "New";

    [Range(0.0, 100000000.0, ErrorMessage = "Expected Value must be between 0 and 100000000.")]
    [Display(Name = "Expected Value")]
    public decimal ExpectedValue { get; set; }

    [Display(Name = "Assigned To")]
    public string? AssignedTo { get; set; }
}
