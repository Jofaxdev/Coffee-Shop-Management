using System.Collections.Generic;

namespace Coffee_Shop_Management.Areas.Pos.Models
{
    public class OrderItemViewModel
    {
        public int Id { get; set; }
        public int Quantity { get; set; }
        public string Note { get; set; }
        public decimal Price { get; set; }
    }

    public class OrderViewModel
    {
        public string TableId { get; set; }
        public List<OrderItemViewModel> Items { get; set; }
        public int StatusPayment { get; set; }
    }

    // ViewModel cho chức năng chuyển bàn
    public class MoveTableVM
    {
        public string FromTableCode { get; set; }
        public string ToTableCode { get; set; }
    }

    // ViewModel cho chức năng thanh toán
    public class CheckoutVM
    {
        public string TableCode { get; set; }
        public decimal CashReceived { get; set; }
    }

    // ViewModel cho chức năng tách hóa đơn
    public class SplitBillVM
    {
        public string SourceTableCode { get; set; }
        public string DestinationTableCode { get; set; }
        public List<OrderItemViewModel> ItemsToMove { get; set; }
    }

    // *** BỔ SUNG VIEWMODEL MỚI CHO GỘP BÀN ***
    public class MergeTablesVM
    {
        public string DestinationTableCode { get; set; }
        public List<string> SourceTableCodes { get; set; }
    }
}