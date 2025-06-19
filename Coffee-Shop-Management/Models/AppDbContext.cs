using Humanizer;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualBasic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Net.NetworkInformation;

namespace Coffee_Shop_Management.Models
{
    public class AppDbContext : IdentityDbContext<AppUser>
    {
        //Tạo các DbSet (tự động kết nối các bảng dữ liệu trong CSDL)

        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        protected override void OnConfiguring(DbContextOptionsBuilder builder)
        {
            base.OnConfiguring(builder);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Loại bỏ tiền tố "AspNet" cho các bảng trong Identity nếu có
            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                var tableName = entityType.GetTableName();
                if (tableName != null && tableName.StartsWith("AspNet"))
                {
                    entityType.SetTableName(tableName.Substring(6));
                }
            }

            // Cấu hình quan hệ many-to-many cho Product và Category
            modelBuilder.Entity<ProductCategory>()
                .HasKey(pc => new { pc.ProductId, pc.CategoryId }); // Khóa chính

            modelBuilder.Entity<ProductCategory>()
                .HasOne(pc => pc.Product)
                .WithMany(p => p.ProductCategorys)
                .HasForeignKey(pc => pc.ProductId);

            modelBuilder.Entity<ProductCategory>()
                .HasOne(pc => pc.Category)
                .WithMany(c => c.ProductCategories)
                .HasForeignKey(pc => pc.CategoryId);

            // === FIX: BỎ CẤU HÌNH KHÓA CHÍNH PHỨC HỢP SAI ===
            // Dòng code dưới đây là nguyên nhân chính gây ra lỗi mất ghi chú.
            // Bằng việc xóa nó đi, chúng ta cho phép EF Core sử dụng cột "Id" làm khóa chính duy nhất,
            // cho phép lưu nhiều sản phẩm cùng loại với các ghi chú khác nhau trong cùng một đơn hàng.
            /*
            modelBuilder.Entity<OrderDetail>()
               .HasKey(od => new { od.OrderId, od.ProductId });
            */
            // === KẾT THÚC SỬA LỖI ===


            // Cấu hình cho EmployeeAssignment
            modelBuilder.Entity<EmployeeAssignment>(entity =>
            {
                entity.Property(e => e.WorkDate).HasColumnType("date");

                entity.HasOne(d => d.Employee)
                    .WithMany()
                    .HasForeignKey(d => d.EmployeeId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(d => d.Shift)
                    .WithMany(p => p.EmployeeAssignments)
                    .HasForeignKey(d => d.ShiftId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(d => d.AssignedByUser)
                   .WithMany()
                   .HasForeignKey(d => d.AssignedByUserId)
                   .OnDelete(DeleteBehavior.ClientSetNull);

                entity.HasOne(d => d.ApprovedByUser)
                   .WithMany()
                   .HasForeignKey(d => d.ApprovedByUserId)
                   .OnDelete(DeleteBehavior.ClientSetNull);

                entity.Property(e => e.WorkedHours).HasPrecision(5, 2);
                entity.Property(e => e.WageRateSnapshot).HasPrecision(18, 2);
                entity.Property(e => e.FinalWage).HasPrecision(18, 2);

                entity.HasIndex(e => new { e.WorkDate, e.EmployeeId });
                entity.HasIndex(e => new { e.WorkDate, e.ShiftId });
            });

            // Cấu hình cho Shift
            modelBuilder.Entity<Shift>(entity =>
            {
                entity.HasIndex(e => e.Name).IsUnique();
            });

            modelBuilder.Entity<Area>(entity =>
            {
                entity.HasIndex(e => e.Name).IsUnique();
                entity.Property(e => e.DisplayOrder).HasDefaultValue(0);

                entity.HasMany(a => a.Tables)
                      .WithOne(t => t.Area)
                      .HasForeignKey(t => t.AreaCode)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<Table>(entity =>
            {
                entity.HasIndex(t => t.AreaCode);
                entity.Property(t => t.DisplayOrder).HasDefaultValue(0);
            });

            modelBuilder.Entity<Order>(entity => {
                entity.Property(o => o.Discount).HasPrecision(18, 2);
                entity.Property(o => o.VAT).HasPrecision(18, 2);
                entity.Property(o => o.TotalAmount).HasPrecision(18, 2);
                entity.Property(o => o.CustomerMoney).HasPrecision(18, 2);
                entity.Property(o => o.MoneyToCustomer).HasPrecision(18, 2);

                entity.HasOne(o => o.Table)
                      .WithMany(t => t.Orders)
                      .HasForeignKey(o => o.TableCode)
                      .OnDelete(DeleteBehavior.SetNull);
            });

            modelBuilder.Entity<Product>(entity => {
                entity.Property(p => p.CostPrice).HasPrecision(16, 2);
                entity.Property(p => p.UnitPrice).HasPrecision(16, 2);
            });

            modelBuilder.Entity<OrderDetail>(entity => {
                entity.Property(od => od.UnitPrice).HasPrecision(18, 2);
            });

            modelBuilder.Entity<IncomeAndExpenditure>(entity => {
                entity.Property(ie => ie.Amount).HasPrecision(18, 2);
            });

            modelBuilder.Entity<ProductIngredient>()
                .HasKey(pi => new { pi.ProductId, pi.IngredientId });

            modelBuilder.Entity<ProductIngredient>()
                .HasOne(pi => pi.Product)
                .WithMany(p => p.ProductIngredients)
                .HasForeignKey(pi => pi.ProductId);

            modelBuilder.Entity<ProductIngredient>()
                .HasOne(pi => pi.Ingredient)
                .WithMany()
                .HasForeignKey(pi => pi.IngredientId);

            modelBuilder.Entity<Ingredient>(entity =>
            {
                entity.Property(e => e.CurrentStockLevel).HasPrecision(18, 3);
                entity.Property(e => e.MinimumStockLevel).HasPrecision(18, 3);
                entity.Property(e => e.LastPurchasePrice).HasPrecision(18, 2);
            });

            modelBuilder.Entity<Supplier>(entity =>
            {
                entity.HasIndex(e => e.Name).IsUnique();
            });

            modelBuilder.Entity<InventoryTransaction>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.QuantityChanged).HasPrecision(18, 4);
                entity.Property(e => e.UnitPrice).HasPrecision(18, 2);
                entity.Property(e => e.TotalPrice).HasPrecision(18, 2);

                entity.HasOne(it => it.Ingredient)
                   .WithMany()
                   .HasForeignKey(it => it.IngredientId)
                   .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(it => it.Supplier)
                   .WithMany()
                   .HasForeignKey(it => it.SupplierId)
                   .IsRequired(false)
                   .OnDelete(DeleteBehavior.SetNull);

                entity.HasOne(it => it.User)
                   .WithMany()
                   .HasForeignKey(it => it.UserId)
                   .IsRequired(false)
                   .OnDelete(DeleteBehavior.SetNull);

                entity.HasIndex(e => new { e.IngredientId, e.TransactionDate });
                entity.HasIndex(e => e.TransactionType);
            });
        }

        public DbSet<AppUser> AppUsers { get; set; }
        public DbSet<Product> Products { get; set; }
        public DbSet<Category> Categories { get; set; }
        public DbSet<Order> Orders { get; set; }
        public DbSet<OrderDetail> OrderDetails { get; set; }
        public DbSet<IncomeAndExpenditure> IncomeAndExpenditures { get; set; }
        public DbSet<UnitOfMeasure> UnitOfMeasures { get; set; }
        public DbSet<Area> Areas { get; set; }
        public DbSet<Table> Tables { get; set; }
        public DbSet<ProductCategory> ProductCategorys { get; set; }
        public DbSet<Shift> Shifts { get; set; }
        public DbSet<EmployeeAssignment> EmployeeAssignments { get; set; }
        public DbSet<Ingredient> Ingredients { get; set; }
        public DbSet<InventoryBatch> InventoryBatches { get; set; }
        public DbSet<Supplier> Suppliers { get; set; }
        public DbSet<InventoryTransaction> InventoryTransactions { get; set; }
        public DbSet<ProductIngredient> ProductIngredients { get; set; }
        public DbSet<Notification> Notifications { get; set; }


        public class Notification
        {
            [Key]
            public int Id { get; set; }

            [Required]
            [MaxLength(50)]
            public string TableCode { get; set; } = string.Empty;

            [Required]
            [MaxLength(100)]
            public string TableName { get; set; } = string.Empty;

            [Required]
            [MaxLength(50)]
            public string RequestType { get; set; } // "Order" hoặc "Payment"

            [Required]
            [MaxLength(255)]
            public string Message { get; set; } = string.Empty;

            public DateTime CreatedAt { get; set; } = DateTime.Now;

            public bool IsRead { get; set; } = false;

            public DateTime? ReadAt { get; set; }

            public string? AcknowledgedByUserId { get; set; }

            [ForeignKey("AcknowledgedByUserId")]
            public virtual AppUser? AcknowledgedByUser { get; set; }

            public string? AcknowledgedByUserName { get; set; }
        }

        public class Ingredient
        {
            public int Id { get; set; }
            [Required]
            [MaxLength(150)]
            public string Name { get; set; } = string.Empty;
            [MaxLength(500)]
            public string? Description { get; set; }
            [Required]
            public string UnitOfMeasure { get; set; } = "";
            [Precision(18, 3)]
            public decimal CurrentStockLevel { get; set; } = 0;
            [Precision(18, 3)]
            public decimal? MinimumStockLevel { get; set; }
            [Precision(18, 2)]
            public decimal LastPurchasePrice { get; set; }
            public bool IsActive { get; set; } = true;
            public DateTime CreatedAt { get; set; } = DateTime.Now;
            public DateTime UpdatedAt { get; set; } = DateTime.Now;
            public bool DeleteTemp { get; set; } = false;
        }

        public class InventoryBatch
        {
            [Key]
            public int Id { get; set; }
            [Required]
            public int IngredientId { get; set; }
            [ForeignKey("IngredientId")]
            public virtual Ingredient? Ingredient { get; set; }
            [MaxLength(50)]
            public string BatchNumber { get; set; } = string.Empty;
            [Required]
            [Precision(18, 4)]
            public decimal Quantity { get; set; }
            [Required]
            [Precision(18, 2)]
            public decimal PurchasePrice { get; set; }
            [Required]
            public DateTime PurchaseDate { get; set; }
            public DateTime? ExpiryDate { get; set; }
            public bool IsActive { get; set; } = true;
        }

        public class ProductIngredient
        {
            public int ProductId { get; set; }
            public virtual Product? Product { get; set; }
            public int IngredientId { get; set; }
            public virtual Ingredient? Ingredient { get; set; }
            [Required]
            [Precision(18, 4)]
            public decimal QuantityNeeded { get; set; }
        }

        public class Supplier
        {
            public int Id { get; set; }
            [Required]
            [MaxLength(150)]
            public string Name { get; set; } = string.Empty;
            [MaxLength(100)]
            public string? ContactPerson { get; set; }
            [MaxLength(20)]
            public string? PhoneNumber { get; set; }
            [MaxLength(100)]
            public string? Email { get; set; }
            [MaxLength(250)]
            public string? Address { get; set; }
            [MaxLength(500)]
            public string? Notes { get; set; }
            public DateTime CreatedAt { get; set; } = DateTime.Now;
            public DateTime UpdatedAt { get; set; } = DateTime.Now;
            public bool DeleteTemp { get; set; } = false;
        }

        public enum InventoryTransactionType
        {
            InitialStock,
            Purchase,
            SaleConsumption,
            AdjustmentIn,
            AdjustmentOut
        }

        public class InventoryTransaction
        {
            public long Id { get; set; }
            [Required]
            public int IngredientId { get; set; }
            [ForeignKey("IngredientId")]
            public virtual Ingredient? Ingredient { get; set; }
            [Required]
            public InventoryTransactionType TransactionType { get; set; }
            public DateTime TransactionDate { get; set; } = DateTime.Now;
            [Precision(18, 4)]
            public decimal QuantityChanged { get; set; }
            [Precision(18, 2)]
            public decimal? UnitPrice { get; set; }
            [Precision(18, 2)]
            public decimal? TotalPrice { get; set; }
            public int? SupplierId { get; set; }
            [ForeignKey("SupplierId")]
            public virtual Supplier? Supplier { get; set; }
            public string? UserId { get; set; }
            [ForeignKey("UserId")]
            public virtual AppUser? User { get; set; }
            [MaxLength(50)]
            public string? VoucherCode { get; set; }
            [MaxLength(500)]
            public string? Notes { get; set; }
            public DateTime CreatedAt { get; set; } = DateTime.Now;
            public int? BatchId { get; set; }
            [ForeignKey("BatchId")]
            public virtual InventoryBatch? Batch { get; set; }
        }

        public class Product
        {
            public int ProductId { get; set; }
            [MaxLength(100)]
            public string Name { get; set; } = "";
            [Precision(16, 2)]
            public decimal CostPrice { get; set; }
            [Precision(16, 2)]
            public decimal UnitPrice { get; set; }
            public string UnitOfMeasure { get; set; } = "";
            public string Description { get; set; } = "";
            public string ImageUrl { get; set; } = "";
            public bool IsActive { get; set; }
            public bool DeleteTemp { get; set; } = false;
            public DateTime CreatedAt { get; set; } = DateTime.Now;
            public DateTime UpdatedAt { get; set; } = DateTime.Now;
            public virtual ICollection<ProductIngredient> ProductIngredients { get; set; } = new List<ProductIngredient>();
            public ICollection<ProductCategory> ProductCategorys { get; set; } = new List<ProductCategory>();
        }

        public class Order
        {
            public int Id { get; set; }
            public DateTime CreatedAt { get; set; } = DateTime.Now;
            [Precision(18, 2)]
            public decimal Discount { get; set; }
            [Precision(18, 2)]
            public decimal VAT { get; set; }
            [Precision(18, 2)]
            public decimal TotalAmount { get; set; }
            [Precision(18, 2)]
            public decimal CustomerMoney { get; set; }
            [Precision(18, 2)]
            public decimal MoneyToCustomer { get; set; }
            public string? Note { get; set; }

            public string? TableCode { get; set; }

            [ForeignKey("TableCode")]
            public virtual Table? Table { get; set; }

            [MaxLength(100)]
            [Required(ErrorMessage = "Tên bàn lịch sử là bắt buộc.")]
            public string TableName { get; set; } = string.Empty;
            [MaxLength(100)]
            [Required(ErrorMessage = "Tên khu vực lịch sử là bắt buộc.")]
            public string AreaName { get; set; } = string.Empty;
            public string? InvoiceCreatorId { get; set; }
            public string? InvoiceCreatorName { get; set; }
            public int StatusPayment { get; set; } = 1;
            public DateTime UpdatedAt { get; set; } = DateTime.Now;
            public ICollection<OrderDetail> OrderDetails { get; set; } = new List<OrderDetail>();
        }

        public class OrderDetail
        {
            // === FIX: Thêm [Key] để đảm bảo đây là khóa chính ===
            [Key]
            public int Id { get; set; }
            public int OrderId { get; set; }
            [ForeignKey("OrderId")]
            public Order? Order { get; set; }
            public int ProductId { get; set; }
            public string NameProduct { get; set; } = string.Empty;
            public int Quantity { get; set; }
            [Precision(18, 2)]
            public decimal UnitPrice { get; set; }
            public string UnitOfMeasure { get; set; } = string.Empty;
            [Precision(18, 2)]
            public decimal TotalPrice { get; set; }
            public string? Note { get; set; }
        }

        public class IncomeAndExpenditure
        {
            public int Id { get; set; }
            public DateTime CreatedAt { get; set; } = DateTime.Now;
            public DateTime UpdatedAt { get; set; } = DateTime.Now;
            [Precision(18, 2)]
            public decimal Amount { get; set; }
            public string GroupIncomeAndExpenditure { get; set; } = string.Empty;
            public bool Type { get; set; } = false;
            public string? CreatorId { get; set; }
            public string? CreatorName { get; set; }
            public string? Note { get; set; }
            public int IsApproved { get; set; } = 0;
        }

        public class UnitOfMeasure
        {
            public int Id { get; set; }
            [Required]
            [MaxLength(50)]
            public string NameUnitOfMeasure { get; set; } = string.Empty;
            public bool IsActive { get; set; } = true;
            public DateTime CreatedAt { get; set; } = DateTime.Now;
            public DateTime UpdatedAt { get; set; } = DateTime.Now;
            public bool DeleteTemp { get; set; } = false;
        }

        public class Area
        {
            [Key]
            [MaxLength(50)]
            public string AreaCode { get; set; } = string.Empty;

            [Required]
            [MaxLength(100)]
            public string Name { get; set; } = string.Empty;

            [MaxLength(250)]
            public string? Description { get; set; }

            public int DisplayOrder { get; set; }

            public bool IsActive { get; set; } = true;
            public DateTime CreatedAt { get; set; } = DateTime.Now;
            public DateTime UpdatedAt { get; set; } = DateTime.Now;
            public virtual ICollection<Table> Tables { get; set; } = new List<Table>();
        }

        public class Table
        {
            [Key]
            [MaxLength(50)]
            public string TableCode { get; set; } = string.Empty;

            [Required]
            [MaxLength(50)]
            public string NameTable { get; set; } = string.Empty;

            public int DisplayOrder { get; set; }

            public bool IsAvailable { get; set; } = true;
            public bool IsActive { get; set; } = true;
            public int Request { get; set; } = 0;

            public DateTime UpdatedAt { get; set; } = DateTime.Now;

            [Required]
            public string AreaCode { get; set; } = string.Empty;

            [ForeignKey("AreaCode")]
            public virtual Area? Area { get; set; }
            public virtual ICollection<Order> Orders { get; set; } = new List<Order>();
        }

        public class Category
        {
            public int Id { get; set; }
            public string NameCategory { get; set; } = string.Empty;
            public bool IsActive { get; set; } = true;
            public DateTime CreatedAt { get; set; } = DateTime.Now;
            public DateTime UpdatedAt { get; set; } = DateTime.Now;
            public bool DeleteTemp { get; set; } = false;
            public ICollection<ProductCategory> ProductCategories { get; set; } = new List<ProductCategory>();
        }

        public class ProductCategory
        {
            public int ProductId { get; set; }
            public Product? Product { get; set; }
            public int CategoryId { get; set; }
            public Category? Category { get; set; }
        }

        public class Shift
        {
            [Key]
            public int ShiftId { get; set; }
            [Required]
            [MaxLength(50)]
            public string Name { get; set; } = string.Empty;
            [Required]
            public TimeSpan StartTime { get; set; }
            [Required]
            public TimeSpan EndTime { get; set; }
            public virtual ICollection<EmployeeAssignment> EmployeeAssignments { get; set; } = new List<EmployeeAssignment>();
        }

        public class EmployeeAssignment
        {
            [Key]
            public int AssignmentId { get; set; }
            [Required]
            public string EmployeeId { get; set; } = string.Empty;
            [ForeignKey("EmployeeId")]
            public virtual AppUser? Employee { get; set; }
            [Required]
            public int ShiftId { get; set; }
            [ForeignKey("ShiftId")]
            public virtual Shift? Shift { get; set; }
            [Required]
            [DataType(DataType.Date)]
            public DateTime WorkDate { get; set; }
            [Required]
            public TimeSpan AssignedStartTime { get; set; }
            [Required]
            public TimeSpan AssignedEndTime { get; set; }
            public string? AssignedByUserId { get; set; }
            [ForeignKey("AssignedByUserId")]
            public virtual AppUser? AssignedByUser { get; set; }
            public DateTime AssignmentCreatedAt { get; set; } = DateTime.Now;
            public DateTime? ActualClockIn { get; set; }
            public DateTime? ActualClockOut { get; set; }
            [MaxLength(50)]
            public string AttendanceStatus { get; set; } = "Chưa vào ca";
            [Precision(18, 2)]
            public decimal? WageRateSnapshot { get; set; }
            [Precision(5, 2)]
            public decimal? WorkedHours { get; set; }
            [Precision(18, 2)]
            public decimal? FinalWage { get; set; }
            [MaxLength(50)]
            public string ApprovalStatus { get; set; } = "Chờ duyệt";
            public string? ApprovedByUserId { get; set; }
            [ForeignKey("ApprovedByUserId")]
            public virtual AppUser? ApprovedByUser { get; set; }
            public DateTime? ApprovedAt { get; set; }
            public string? ApprovalNote { get; set; }
        }
    }
}