namespace Coffee_Shop_Management.Areas.Pos.Models
{
    public class ProductViewModel
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public decimal Price { get; set; }
        public string Image { get; set; }
        public string Category { get; set; }
    }

    public class TableViewModel
    {
        // *** THAY ĐỔI: Chuyển từ int sang string để chứa TableCode ***
        public string Id { get; set; }
        public string Name { get; set; }
        public string Status { get; set; }
        public int Request { get; set; } // <-- THÊM DÒNG NÀY
    }

    public class AreaViewModel
    {
        public string Name { get; set; }
        public List<TableViewModel> Tables { get; set; }
    }

    public class PosDataViewModel
    {
        public List<ProductViewModel> Products { get; set; }
        public List<string> Categories { get; set; }
        public List<AreaViewModel> Areas { get; set; }
    }
}