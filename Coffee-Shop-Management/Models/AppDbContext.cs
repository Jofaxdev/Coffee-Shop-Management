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
            //Database.EnsureCreated();   // Tự động tạo database nếu database không tồn tại
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
                if (tableName.StartsWith("AspNet"))
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

            modelBuilder.Entity<OrderDetail>()
               .HasKey(od => new { od.OrderId, od.ProductId });

            // Cấu hình cho EmployeeAssignment
            modelBuilder.Entity<EmployeeAssignment>(entity =>
            {
                entity.Property(e => e.WorkDate).HasColumnType("date");

                // FK đến Employee (Người làm việc) - Restrict/NoAction thường là an toàn
                entity.HasOne(d => d.Employee)
                    .WithMany()
                    .HasForeignKey(d => d.EmployeeId)
                    .OnDelete(DeleteBehavior.Restrict); // Giữ nguyên Restrict (NO ACTION)

                // FK đến Shift - Restrict/NoAction thường là an toàn
                entity.HasOne(d => d.Shift)
                    .WithMany(p => p.EmployeeAssignments)
                    .HasForeignKey(d => d.ShiftId)
                    .OnDelete(DeleteBehavior.Restrict); // Giữ nguyên Restrict (NO ACTION)

                // FK đến AssignedByUser (Người phân công)
                entity.HasOne(d => d.AssignedByUser)
                   .WithMany()
                   .HasForeignKey(d => d.AssignedByUserId)
                   // THAY ĐỔI Ở ĐÂY: Sử dụng ClientSetNull hoặc NoAction
                   .OnDelete(DeleteBehavior.ClientSetNull); // Hoặc .OnDelete(DeleteBehavior.NoAction);

                // FK đến ApprovedByUser (Người duyệt)
                entity.HasOne(d => d.ApprovedByUser)
                    .WithMany()
                    .HasForeignKey(d => d.ApprovedByUserId)
                    // THAY ĐỔI Ở ĐÂY: Sử dụng ClientSetNull hoặc NoAction
                    .OnDelete(DeleteBehavior.ClientSetNull); // Hoặc .OnDelete(DeleteBehavior.NoAction);


                // --- Sửa lỗi Precision ---
                // Kiểm tra lại Precision trong Model và đảm bảo chúng hợp lý
                // Ví dụ: WorkedHours nên có Precision lớn hơn (2,2)
                entity.Property(e => e.WorkedHours)
                      .HasPrecision(5, 2); // Sửa thành (5, 2) cho phép lưu 999.99 giờ


                // Có thể khai báo các Precision khác ở đây nếu không dùng Attributes trong Model
                entity.Property(e => e.WageRateSnapshot).HasPrecision(18, 2);
                entity.Property(e => e.FinalWage).HasPrecision(18, 2);


                // Indexes (Giữ nguyên)
                entity.HasIndex(e => new { e.WorkDate, e.EmployeeId });
                entity.HasIndex(e => new { e.WorkDate, e.ShiftId });
            });

            // Cấu hình cho Shift (Giữ nguyên)
            modelBuilder.Entity<Shift>(entity =>
            {
                entity.HasIndex(e => e.Name).IsUnique();
            });

            // *** CẤU HÌNH AREA VÀ TABLE (XÓA VẬT LÝ) ***
            // Area
            modelBuilder.Entity<Area>(entity =>
            {
                entity.HasIndex(e => e.Name).IsUnique();
                entity.HasMany(a => a.Tables)
                      .WithOne(t => t.Area)
                      .HasForeignKey(t => t.AreaId)
                      // >>> Ngăn xóa Area nếu còn Table <<<
                      .OnDelete(DeleteBehavior.Restrict); // Buộc xóa Table thủ công trước
            });

            // Table
            modelBuilder.Entity<Table>(entity =>
            {
                entity.HasIndex(t => t.AreaId);
                entity.HasMany(t => t.Orders)
                      .WithOne(o => o.Table)
                      .HasForeignKey(o => o.TableId) 
                      .OnDelete(DeleteBehavior.Restrict);
            });
            // *** KẾT THÚC CẤU HÌNH AREA/TABLE (XÓA VẬT LÝ) ***

            // Cấu hình Precision
            modelBuilder.Entity<Product>(entity => {
                entity.Property(p => p.CostPrice).HasPrecision(16, 2);
                entity.Property(p => p.UnitPrice).HasPrecision(16, 2);
            });
            modelBuilder.Entity<Order>(entity => {
                entity.Property(o => o.Discount).HasPrecision(18, 2);
                entity.Property(o => o.VAT).HasPrecision(18, 2);
                entity.Property(o => o.TotalAmount).HasPrecision(18, 2);
                entity.Property(o => o.CustomerMoney).HasPrecision(18, 2);
                entity.Property(o => o.MoneyToCustomer).HasPrecision(18, 2);
            });
            modelBuilder.Entity<OrderDetail>(entity => {
                entity.Property(od => od.UnitPrice).HasPrecision(18, 2);
            });
            modelBuilder.Entity<IncomeAndExpenditure>(entity => {
                entity.Property(ie => ie.Amount).HasPrecision(18, 2);
            });


            // Cấu hình cho ProductIngredient
            // Cấu hình ProductIngredient (Many-to-Many giữa Product và Ingredient cho công thức)
            modelBuilder.Entity<ProductIngredient>()
                .HasKey(pi => new { pi.ProductId, pi.IngredientId });

            modelBuilder.Entity<ProductIngredient>()
                .HasOne(pi => pi.Product)
                .WithMany(p => p.ProductIngredients) // Đảm bảo Product có ICollection<ProductIngredient> ProductIngredients
                .HasForeignKey(pi => pi.ProductId);

            modelBuilder.Entity<ProductIngredient>()
                .HasOne(pi => pi.Ingredient)
                .WithMany() // Ingredient không cần collection ngược lại ProductIngredients
                .HasForeignKey(pi => pi.IngredientId);

            // --- CẤU HÌNH CHO QUẢN LÝ KHO MỚI ---
            modelBuilder.Entity<Ingredient>(entity =>
            {
                entity.Property(e => e.CurrentStockLevel).HasPrecision(18, 3);
                entity.Property(e => e.MinimumStockLevel).HasPrecision(18, 3);
                entity.Property(e => e.LastPurchasePrice).HasPrecision(18, 2);
            });

            modelBuilder.Entity<Supplier>(entity =>
            {
                // Cấu hình cho Supplier nếu cần thêm (ví dụ: unique constraints)
                entity.HasIndex(e => e.Name).IsUnique(); // Ví dụ: Tên nhà cung cấp là duy nhất
            });

            modelBuilder.Entity<InventoryTransaction>(entity =>
            {
                entity.HasKey(e => e.Id);

                entity.Property(e => e.QuantityChanged).HasPrecision(18, 4);
                entity.Property(e => e.UnitPrice).HasPrecision(18, 2);
                entity.Property(e => e.TotalPrice).HasPrecision(18, 2);

                entity.HasOne(it => it.Ingredient)
                    .WithMany() // Ingredient không cần collection ngược lại InventoryTransactions
                    .HasForeignKey(it => it.IngredientId)
                    .OnDelete(DeleteBehavior.Restrict); // Không cho xóa Ingredient nếu có giao dịch

                entity.HasOne(it => it.Supplier)
                    .WithMany() // Supplier không cần collection ngược lại InventoryTransactions
                    .HasForeignKey(it => it.SupplierId)
                    .IsRequired(false) // SupplierId là tùy chọn
                    .OnDelete(DeleteBehavior.SetNull); // Nếu Supplier bị xóa, set FK thành null

                entity.HasOne(it => it.User) // AppUser thực hiện giao dịch
                    .WithMany() // AppUser không cần collection ngược lại InventoryTransactions
                    .HasForeignKey(it => it.UserId)
                    .IsRequired(false) // UserId có thể là null (ví dụ: giao dịch hệ thống)
                    .OnDelete(DeleteBehavior.SetNull); // Hoặc Restrict nếu User luôn phải tồn tại

                entity.HasIndex(e => new { e.IngredientId, e.TransactionDate });
                entity.HasIndex(e => e.TransactionType);
            });
            // --- KẾT THÚC CẤU HÌNH KHO MỚI ---

        }

        public DbSet<AppUser>? AppUsers { get; set; }
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
        // DbSets cho quản lý kho mới
        public DbSet<Ingredient> Ingredients { get; set; }
        public DbSet<Supplier> Suppliers { get; set; }
        public DbSet<InventoryTransaction> InventoryTransactions { get; set; }
        public DbSet<ProductIngredient> ProductIngredients { get; set; }

        //Kho start
        public class Ingredient
        {
            public int Id { get; set; }

            [Required]
            [MaxLength(150)]
            public string Name { get; set; } = string.Empty;

            [MaxLength(500)]
            public string? Description { get; set; }
            [Required]
            public string UnitOfMeasure { get; set; } = ""; //Đơn vị tính

            [Precision(18, 3)]
            public decimal CurrentStockLevel { get; set; } = 0;

            [Precision(18, 3)]
            public decimal? MinimumStockLevel { get; set; }

            [Precision(18, 2)]
            public decimal LastPurchasePrice { get; set; } // Giá nhập kho lần cuối

            public bool IsActive { get; set; } = true;
            public DateTime CreatedAt { get; set; } = DateTime.Now;
            public DateTime UpdatedAt { get; set; } = DateTime.Now;
            public bool DeleteTemp { get; set; } = false;
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
            InitialStock,       // Nhập tồn kho ban đầu
            Purchase,           // Nhập kho từ mua hàng
            SaleConsumption,    // Xuất kho do bán hàng
            AdjustmentIn,       // Điều chỉnh tăng (vd: kiểm kê thấy thừa, nhập trả lại)
            AdjustmentOut       // Điều chỉnh giảm (vd: hỏng, hủy, thất thoát, kiểm kê thiếu)
        }

        public class InventoryTransaction
        {
            public long Id { get; set; } // Sử dụng long nếu số lượng giao dịch lớn

            [Required]
            public int IngredientId { get; set; }
            [ForeignKey("IngredientId")]
            public virtual Ingredient? Ingredient { get; set; }

            [Required]
            public InventoryTransactionType TransactionType { get; set; }

            public DateTime TransactionDate { get; set; } = DateTime.Now;

            // Số lượng thay đổi: dương cho nhập, âm cho xuất
            [Precision(18, 4)]
            public decimal QuantityChanged { get; set; }

            // Đơn giá tại thời điểm giao dịch (quan trọng cho Purchase)
            [Precision(18, 2)]
            public decimal? UnitPrice { get; set; }

            // Tổng tiền cho giao dịch này (tính toán hoặc lưu trữ)
            [Precision(18, 2)]
            public decimal? TotalPrice { get; set; } // Có thể là QuantityChanged * UnitPrice cho Purchase

            public int? SupplierId { get; set; } // Chỉ dùng cho TransactionType.Purchase
            [ForeignKey("SupplierId")]
            public virtual Supplier? Supplier { get; set; }

            public string? UserId { get; set; } // ID người thực hiện/liên quan đến giao dịch
            [ForeignKey("UserId")]
            public virtual AppUser? User { get; set; }

            [MaxLength(500)]
            public string? Notes { get; set; } // Ghi chú (vd: "Hủy do bể vỡ", "Kiểm kê ngày X")

            public DateTime CreatedAt { get; set; } = DateTime.Now;
        }
        //Kho end


        public class Product
        {
            public int ProductId { get; set; }
            [MaxLength(100)]
            public string Name { get; set; } = "";
            [Precision(16, 2)]
            public decimal CostPrice { get; set; } //Giá vốn
            [Precision(16, 2)]
            public decimal UnitPrice { get; set; } //Đơn giá
            public string UnitOfMeasure { get; set; } = ""; //Đơn vị tính
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
            public decimal Discount { get; set; } //Giảm giá
            [Precision(18, 2)]
            public decimal VAT { get; set; }
            [Precision(18, 2)]
            public decimal TotalAmount { get; set; } //Tổng tiền
            [Precision(18, 2)]
            public decimal CustomerMoney { get; set; } //Tiền khách đưa
            [Precision(18, 2)]
            public decimal MoneyToCustomer { get; set; } //Tiền thừa trả lại khách
            public string? Note { get; set; }  //Ghi chú
            public int? TableId { get; set; } // Nullable vì Table có thể bị xóa

            [ForeignKey("TableId")]
            public virtual Table? Table { get; set; } // Navigation property
            [MaxLength(100)]
            [Required(ErrorMessage = "Tên bàn lịch sử là bắt buộc.")]
            public string TableName { get; set; } = string.Empty; // Phải lưu tên bàn lúc tạo order
            [MaxLength(100)]
            [Required(ErrorMessage = "Tên khu vực lịch sử là bắt buộc.")]
            public string AreaName { get; set; } = string.Empty;
            public string? InvoiceCreatorId { get; set; } // ID người tạo hóa đơn
            public string? InvoiceCreatorName { get; set; } // Name người  tạo hóa đơn
            public int StatusPayment { get; set; } = 1; // 1 là đã thanh toán, 0 là chưa thanh toán, 2 là đang xử lý 
            public ICollection<OrderDetail> OrderDetails { get; set; } = new List<OrderDetail>();
        }

        public class OrderDetail
        {
            public int Id { get; set; }
            public int OrderId { get; set; } // Khóa ngoại đến Order
            [ForeignKey("OrderId")]
            public Order? Order { get; set; } // Navigation property
            [MaxLength(20)]
            public int ProductId { get; set; }
            public string NameProduct { get; set; } = string.Empty; // Tên sản phẩm
            public int Quantity { get; set; }
            [Precision(18, 2)]
            public decimal UnitPrice { get; set; } // Đơn giá (lấy từ Product, nhưng có thể thay đổi)
            [Precision(18, 2)]
            public string UnitOfMeasure { get; set; } = string.Empty; // Tên sản phẩm
            [Precision(18, 2)]
            public decimal TotalPrice { get; set; } // Thành tiền (Quantity * UnitPrice)
        }

        public class IncomeAndExpenditure
        {
            public int Id { get; set; }
            public DateTime CreatedAt { get; set; } = DateTime.Now;
            public DateTime UpdatedAt { get; set; } = DateTime.Now;
            [Precision(18, 2)]
            public decimal Amount { get; set; }
            public string GroupIncomeAndExpenditure { get; set; } = string.Empty;
            public bool Type { get; set; } = false; //false là chi, true là thu
            public string? CreatorId { get; set; } // ID người tạo
            public string? CreatorName { get; set; } // Name người tạo
            public string? Note { get; set; } // Ghi chú
            public int IsApproved { get; set; } = 0; // 0 là chưa duyệt, 1 là đã duyệt, 2 là từ chối
        }


        public class UnitOfMeasure //Đơn vị tính
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
            public int Id { get; set; }
            [Required]
            [MaxLength(100)]
            public string Name { get; set; } = string.Empty;
            [MaxLength(250)]
            public string? Description { get; set; }
            public bool IsActive { get; set; } = true; 
            public DateTime CreatedAt { get; set; } = DateTime.Now;
            public DateTime UpdatedAt { get; set; } = DateTime.Now;
            public virtual ICollection<Table> Tables { get; set; } = new List<Table>();
        }

        public class Table
        {
            public int Id { get; set; }
            [Required]
            [MaxLength(50)]
            public string NameTable { get; set; } = string.Empty;
            public bool IsAvailable { get; set; } = true;
            public bool IsActive { get; set; } = true; 
            public int Request { get; set; } = 0;
            [Required]
            public int AreaId { get; set; }
            [ForeignKey("AreaId")]
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
            public int ShiftId { get; set; } // Khóa chính

            [Required]
            [MaxLength(50)]
            public string Name { get; set; } = string.Empty; // Tên ca (vd: "Sáng", "Chiều", "Tối")

            [Required]
            public TimeSpan StartTime { get; set; } // Giờ bắt đầu ca (dùng TimeSpan để chỉ lưu giờ:phút:giây)

            [Required]
            public TimeSpan EndTime { get; set; } // Giờ kết thúc ca

            // Navigation property (không bắt buộc, nhưng hữu ích)
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

            // Ngày làm việc
            [Required]
            [DataType(DataType.Date)]
            public DateTime WorkDate { get; set; }

            // Snapshot thời gian ca làm tại thời điểm phân công --- ví dụ ca sáng là 8h-12h
            [Required]
            public TimeSpan AssignedStartTime { get; set; } // Lưu thời gian bắt ca làm tại thời điểm phân công

            [Required]
            public TimeSpan AssignedEndTime { get; set; } // Lưu thời gian kết ca làm tại thời điểm phân công

            // --- Thông tin phân công ---
            public string? AssignedByUserId { get; set; }

            [ForeignKey("AssignedByUserId")]
            public virtual AppUser? AssignedByUser { get; set; }

            public DateTime AssignmentCreatedAt { get; set; } = DateTime.Now;

            // --- Thông tin chấm công của nhân viên ---
            public DateTime? ActualClockIn { get; set; }
            public DateTime? ActualClockOut { get; set; }

            [MaxLength(50)]
            public string AttendanceStatus { get; set; } = "Chưa vào ca";

            // --- Thông tin tính toán giờ và lương ---
            // Mức lương giờ tại thời điểm phân công ca
            [Precision(18, 2)] 
            public decimal? WageRateSnapshot { get; set; }

            // Số giờ làm việc thực tế (tính từ ClockIn/Out)
            [Precision(5, 2)]
            public decimal? WorkedHours { get; set; }

            // Đây là số tiền thực tế trả cho nhân viên cho ca làm này
            [Precision(18, 2)]
            public decimal? FinalWage { get; set; }

            // --- Thông tin duyệt chấm công của quản lý ---
            [MaxLength(50)]
            public string ApprovalStatus { get; set; } = "Chờ duyệt";

            public string? ApprovedByUserId { get; set; }

            [ForeignKey("ApprovedByUserId")]
            public virtual AppUser? ApprovedByUser { get; set; }

            public DateTime? ApprovedAt { get; set; } // Thời gian duyệt
            public string? ApprovalNote { get; set; } // Manager có thể ghi chú lý do chỉnh lương ở đây (vd: "Lễ X2 lương")
        }
    }
}