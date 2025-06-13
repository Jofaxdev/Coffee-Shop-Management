using System.Collections.Generic; // Thêm nếu chưa có

namespace Coffee_Shop_Management.Areas.Admin.ViewModels
{
    public class DataTableRequest
    {
        public int Draw { get; set; }
        public int Start { get; set; }
        public int Length { get; set; }
        public Search? Search { get; set; }
        public List<Order>? Order { get; set; }
        public List<Column>? Columns { get; set; }

        // --- Thuộc tính lọc tùy chỉnh ---

        // Trạng thái
        public string? IsActive { get; set; } // "true", "false", "All", hoặc null

        // Giá bán
        public decimal? UnitPriceFrom { get; set; }
        public decimal? UnitPriceTo { get; set; }

        // Giá vốn (MỚI)
        public decimal? CostPriceFrom { get; set; }
        public decimal? CostPriceTo { get; set; }

        // Ngày tạo (MỚI) - Nhận dạng chuỗi từ input type="date" (yyyy-MM-dd)
        public string? CreatedDateFrom { get; set; }
        public string? CreatedDateTo { get; set; }

        // Loại sản phẩm và Đơn vị tính
        public List<int>? CategoryIds { get; set; } // Danh sách ID loại SP
        public List<string>? UnitOfMeasures { get; set; } // Danh sách đơn vị tính (chuỗi)
    }

    // Các class Search, Order, Column giữ nguyên
    public class Search
    {
        public string? Value { get; set; }
        public bool Regex { get; set; }
    }

    public class Order
    {
        public int Column { get; set; }
        public string? Dir { get; set; } // "asc" or "desc"
    }

    public class Column
    {
        public string? Data { get; set; } // Tên thuộc tính từ JS (ví dụ: "nameProduct")
        public string? Name { get; set; }
        public bool Searchable { get; set; }
        public bool Orderable { get; set; }
        public Search? Search { get; set; }
    }
}