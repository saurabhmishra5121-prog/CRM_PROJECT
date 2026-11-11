using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AcxiomCRM.Models;

public class Lead
{
    public int LeadId { get; set; }

    [StringLength(20)]
    public string LeadCode { get; set; } = "PENDING";

    [Required, StringLength(100)]
    public string LeadName { get; set; } = string.Empty;

    [Required, StringLength(150)]
    public string Email { get; set; } = string.Empty;

    [Required, StringLength(15)]
    public string Phone { get; set; } = string.Empty;

    [StringLength(100)]
    public string? CompanyName { get; set; }

    [StringLength(50)]
    public string Source { get; set; } = "Website";

    [Required, StringLength(20)]
    public string Status { get; set; } = "New";

    [Column(TypeName = "decimal(18,2)")]
    public decimal ExpectedValue { get; set; }

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

    public string? AssignedTo { get; set; }
}
