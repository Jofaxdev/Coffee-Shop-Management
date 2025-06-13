using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace Coffee_Shop_Management.Models
{
    public class AppUser : IdentityUser
    {
        [StringLength(100)]
        [MaxLength(100)]
        [Required]
        public string? Name { get; set; }
        [Column(TypeName = "nvarchar")]
        [StringLength(400)]
        public string Address { get; set; } = string.Empty;
        public bool LockAccount { get; set; } = false;
        public string Mobile { get; set; } = "";

        [MaxLength(100)]
        public string? JobTitle { get; set; } = string.Empty; // Chức vụ (vd: Pha chế, Phục vụ, Quản lý)
        [Column(TypeName = "decimal(18, 2)")] // Sử dụng Column TypeName thay vì Precision ở DbContext
        public decimal? WageRate { get; set; } // Mức lương giờ
        public string ImageUrl { get; set; } = "";
        public bool DeleteTemp { get; set; } = false;
        [DataType(DataType.Date)]
        public DateTime? BirthDate { get; set; }
        public DateTime UpdateAt { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
