using Coffee_Shop_Management.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Coffee_Shop_Management.Areas.Admin.ViewModels; // Đảm bảo namespace đúng cho ViewModels
using Microsoft.Extensions.Logging;
using static Coffee_Shop_Management.Models.AppDbContext; // Để sử dụng các model lồng nhau như Ingredient
using System.Collections.Generic; // Thêm cho List
using System.IO; // Thêm cho MemoryStream
using ClosedXML.Excel; // Thêm cho ClosedXML
using System.Data; // Thêm cho DataTable (nếu cần, nhưng thường không cần với ClosedXML)

namespace Coffee_Shop_Management.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class InventoryController : Controller
    {
        private readonly AppDbContext _context;
        private readonly UserManager<AppUser> _userManager;
        private readonly ILogger<InventoryController> _logger;
        private readonly CultureInfo _vnCulture = new CultureInfo("vi-VN");

        public InventoryController(AppDbContext context, UserManager<AppUser> userManager, ILogger<InventoryController> logger)
        {
            _context = context;
            _userManager = userManager;
            _logger = logger;
        }

        // GET: /Admin/Inventory/
        public IActionResult Index()
        {
            // View sẽ chứa các tab cho Nguyên vật liệu, Nhà cung cấp, Giao dịch kho
            // và modal để kích hoạt xuất báo cáo Excel
            return View();
        }

        #region Xuất Báo cáo Tồn Kho Chi Tiết Excel
        [HttpPost]
        public async Task<IActionResult> ExportInventoryDetailReport(DateTime fromDate, DateTime toDate, List<int> ingredientIds = null)
        {
            _logger.LogInformation("Bắt đầu xuất báo cáo tồn kho tổng hợp và chi tiết từ {FromDate} đến {ToDate}", fromDate, toDate);

            if (toDate < fromDate)
            {
                return BadRequest("Ngày kết thúc không thể sớm hơn ngày bắt đầu.");
            }
            var reportTrueEndDate = toDate.Date.AddDays(1).AddTicks(-1); // Bao gồm cả ngày toDate

            List<Ingredient> ingredientsToQuery;
            if (ingredientIds == null || !ingredientIds.Any())
            {
                ingredientsToQuery = await _context.Ingredients
                                            .Where(i => !i.DeleteTemp && i.IsActive)
                                            .OrderBy(i => i.Name)
                                            .ToListAsync();
            }
            else
            {
                ingredientsToQuery = await _context.Ingredients
                                            .Where(i => ingredientIds.Contains(i.Id) && !i.DeleteTemp)
                                            .OrderBy(i => i.Name)
                                            .ToListAsync();
            }

            if (!ingredientsToQuery.Any())
            {
                return NotFound("Không tìm thấy nguyên vật liệu phù hợp để tạo báo cáo.");
            }

            var summaryReportItems = new List<InventorySummaryReportItemVM>();
            var detailReportItems = new List<InventoryDetailReportItemVM>(); // Danh sách cho các sheet chi tiết
            int sttSummary = 1;

            foreach (var ingredient in ingredientsToQuery)
            {
                // --- Thu thập dữ liệu cho Sheet Chi Tiết (InventoryDetailReportItemVM) ---
                var detailItem = new InventoryDetailReportItemVM
                {
                    IngredientId = ingredient.Id,
                    IngredientCode = $"NVL{ingredient.Id:D4}",
                    IngredientName = ingredient.Name,
                    UnitOfMeasure = ingredient.UnitOfMeasure,
                    MinimumStockLevel = ingredient.MinimumStockLevel,
                    PeriodStartDate = fromDate,
                    PeriodEndDate = toDate.Date
                };

                var transactionsBeforePeriod = await _context.InventoryTransactions
                    .Where(t => t.IngredientId == ingredient.Id && t.TransactionDate < fromDate)
                    .ToListAsync();

                detailItem.OpeningStockQuantity = transactionsBeforePeriod.Sum(t => t.QuantityChanged);
                var lastPurchaseBefore = transactionsBeforePeriod
                    .Where(t => t.QuantityChanged > 0 && t.UnitPrice.HasValue && t.UnitPrice > 0)
                    .OrderByDescending(t => t.TransactionDate).ThenByDescending(t => t.Id)
                    .FirstOrDefault();
                detailItem.OpeningStockUnitPrice = lastPurchaseBefore?.UnitPrice ?? ingredient.LastPurchasePrice;
                // OpeningStockValue sẽ được tính trong VM hoặc khi render

                var transactionsInPeriod = await _context.InventoryTransactions
                    .Include(t => t.User)
                    .Include(t => t.Supplier)
                    .Where(t => t.IngredientId == ingredient.Id && t.TransactionDate >= fromDate && t.TransactionDate <= reportTrueEndDate)
                    .OrderBy(t => t.TransactionDate).ThenBy(t => t.Id)
                    .ToListAsync();

                decimal currentStockForDetail = detailItem.OpeningStockQuantity;
                foreach (var trans in transactionsInPeriod)
                {
                    var transVM = new InventoryTransactionDetailVM
                    {
                        TransactionDate = trans.TransactionDate,
                        TransactionId = trans.Id,
                        TransactionTypeDisplay = GetTransactionTypeDisplayName(trans.TransactionType),
                        UserName = trans.User?.Name ?? (trans.UserId == null ? "Hệ thống" : "N/A"),
                        SupplierName = trans.Supplier?.Name ?? "",
                        Notes = trans.Notes
                    };

                    if (trans.QuantityChanged > 0)
                    {
                        transVM.QuantityIn = trans.QuantityChanged;
                        transVM.UnitPriceIn = trans.UnitPrice;
                        transVM.ValueIn = trans.TotalPrice ?? (trans.QuantityChanged * (trans.UnitPrice ?? 0));
                    }
                    else
                    {
                        transVM.QuantityOut = -trans.QuantityChanged;
                        transVM.UnitPriceOut = trans.UnitPrice ?? (trans.TotalPrice.HasValue && transVM.QuantityOut > 0 ? Math.Abs(trans.TotalPrice.Value / transVM.QuantityOut.Value) : ingredient.LastPurchasePrice);
                        transVM.ValueOut = trans.TotalPrice.HasValue ? Math.Abs(trans.TotalPrice.Value) : (transVM.QuantityOut * transVM.UnitPriceOut);
                    }
                    currentStockForDetail += trans.QuantityChanged;
                    transVM.StockAfterTransaction = currentStockForDetail;
                    detailItem.Transactions.Add(transVM);
                }
                detailItem.ClosingStockQuantity = currentStockForDetail;
                detailItem.ClosingStockUnitPrice = ingredient.LastPurchasePrice;
                // TotalQuantityIn, TotalValueIn etc. được tính bằng getter trong InventoryDetailReportItemVM
                detailReportItems.Add(detailItem);


                // --- Thu thập dữ liệu cho Sheet Tổng Hợp (InventorySummaryReportItemVM) ---
                // Sử dụng các giá trị đã tính cho detailItem để điền vào summaryItem
                summaryReportItems.Add(new InventorySummaryReportItemVM
                {
                    STT = sttSummary++,
                    IngredientCode = detailItem.IngredientCode,
                    IngredientName = detailItem.IngredientName,
                    UnitOfMeasure = detailItem.UnitOfMeasure,
                    // PurchasePrice = ??, // Vẫn cần xác định nguồn gốc "Giá nhập"
                    CostPrice = ingredient.LastPurchasePrice,

                    OpeningStockQuantity = detailItem.OpeningStockQuantity,
                    OpeningStockValue = detailItem.OpeningStockValue, // Giả sử VM đã tính (Qty * Price)

                    PeriodQuantityIn = detailItem.TotalQuantityIn, // Lấy từ getter của detailItem
                    PeriodValueIn = detailItem.TotalValueIn,     // Lấy từ getter của detailItem

                    PeriodQuantityOut = detailItem.TotalQuantityOut,
                    PeriodValueOut = detailItem.TotalValueOut,

                    ClosingStockQuantity = detailItem.ClosingStockQuantity,
                    ClosingStockValue = detailItem.ClosingStockValue // Giả sử VM đã tính
                });
            }

            // Xuất Excel
            using (var workbook = new XLWorkbook())
            {
                // --- TẠO SHEET TỔNG HỢP (SHEET 1) ---
                var summarySheet = workbook.Worksheets.Add("TongHopNhapXuatTon");
                RenderSummarySheet(summarySheet, summaryReportItems, fromDate, toDate.Date);

                // --- TẠO CÁC SHEET CHI TIẾT (SHEET 2 TRỞ ĐI) ---
                foreach (var detailItemData in detailReportItems)
                {
                    // Tạo tên sheet hợp lệ (giới hạn ký tự, không chứa ký tự đặc biệt)
                    string rawSheetName = $"{detailItemData.IngredientCode}_{detailItemData.IngredientName}";
                    string validSheetName = new string(rawSheetName
                                            .Where(c => char.IsLetterOrDigit(c) || c == '_' || c == ' ' || c == '-')
                                            .ToArray())
                                            .Trim();
                    validSheetName = validSheetName.Length > 31 ? validSheetName.Substring(0, 31) : validSheetName;
                    if (workbook.Worksheets.Any(ws => ws.Name.Equals(validSheetName, StringComparison.OrdinalIgnoreCase)))
                    {
                        validSheetName = validSheetName.Substring(0, Math.Min(validSheetName.Length, 28)) + Guid.NewGuid().ToString().Substring(0, 3); // Ensure uniqueness
                    }

                    var detailSheet = workbook.Worksheets.Add(validSheetName);
                    RenderDetailSheetForItem(detailSheet, detailItemData);
                }

                summarySheet.SetTabActive(); // Đặt sheet tổng hợp làm sheet active khi mở file

                using (var stream = new MemoryStream())
                {
                    workbook.SaveAs(stream);
                    var content = stream.ToArray();
                    string fileName = $"BaoCao_TonKho_TongHopVaChiTiet_{DateTime.Now:yyyyMMddHHmmss}.xlsx";
                    return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
                }
            }
        }

        // Hàm helper để render Sheet Tổng Hợp
        private void RenderSummarySheet(IXLWorksheet sheet, List<InventorySummaryReportItemVM> items, DateTime fromDate, DateTime toDate)
        {
            int currentRow = 1;
            // (Code render Sheet Tổng hợp như trong câu trả lời trước của tôi - phần `ExportInventorySummaryReport`)
            // ... Bắt đầu từ tiêu đề "Báo cáo Nhập Xuất Tồn" ...
            // Tiêu đề chính
            sheet.Cell(currentRow, 1).Value = "Báo cáo Nhập Xuất Tồn";
            sheet.Range(currentRow, 1, currentRow, 14).Merge().Style.Font.SetBold(true).Font.SetFontSize(16) // 14 cột
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
            currentRow += 2;

            // Tiêu đề cột chính (dòng 1 của header)
            sheet.Cell(currentRow, 7).Value = "Đầu kỳ";
            sheet.Range(currentRow, 7, currentRow, 8).Merge().Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
            sheet.Cell(currentRow, 9).Value = "Nhập trong kỳ";
            sheet.Range(currentRow, 9, currentRow, 10).Merge().Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
            sheet.Cell(currentRow, 11).Value = "Xuất trong kỳ";
            sheet.Range(currentRow, 11, currentRow, 12).Merge().Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
            sheet.Cell(currentRow, 13).Value = "Tồn cuối kỳ";
            sheet.Range(currentRow, 13, currentRow, 14).Merge().Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
            var mainHeaderRange = sheet.Range(currentRow, 1, currentRow, 14); // Mở rộng đến cột 14
            mainHeaderRange.Style.Font.SetBold(true).Fill.SetBackgroundColor(XLColor.FromHtml("#FFF2CC"));
            currentRow++;

            // Tiêu đề cột chi tiết (dòng 2 của header)
            var headers = new[] { "STT", "Mã hàng", "Tên hàng", "ĐVT", "Giá nhập", "Giá vốn",
                          "Số lượng", "Tiền", "Số lượng", "Tiền", "Số lượng", "Tiền", "Số lượng", "Tiền" };
            for (int i = 0; i < headers.Length; i++)
            {
                sheet.Cell(currentRow, i + 1).Value = headers[i];
            }
            var detailHeaderRange = sheet.Range(currentRow, 1, currentRow, headers.Length);
            detailHeaderRange.Style.Font.SetBold(true).Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
            sheet.Range(currentRow, 1, currentRow, 6).Style.Fill.SetBackgroundColor(XLColor.FromHtml("#E2EFDA"));
            sheet.Range(currentRow, 7, currentRow, 8).Style.Fill.SetBackgroundColor(XLColor.FromHtml("#FDE9D9"));
            sheet.Range(currentRow, 9, currentRow, 10).Style.Fill.SetBackgroundColor(XLColor.FromHtml("#DDEBF7"));
            sheet.Range(currentRow, 11, currentRow, 12).Style.Fill.SetBackgroundColor(XLColor.FromHtml("#FCE4D6"));
            sheet.Range(currentRow, 13, currentRow, 14).Style.Fill.SetBackgroundColor(XLColor.FromHtml("#E2EFDA"));
            mainHeaderRange.Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin).Border.SetInsideBorder(XLBorderStyleValues.Thin);
            detailHeaderRange.Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin).Border.SetInsideBorder(XLBorderStyleValues.Thin);
            currentRow++;

            // Dữ liệu cho sheet tổng hợp
            foreach (var item in items)
            {
                sheet.Cell(currentRow, 1).Value = item.STT;
                sheet.Cell(currentRow, 2).Value = item.IngredientCode;
                sheet.Cell(currentRow, 3).Value = item.IngredientName;
                sheet.Cell(currentRow, 4).Value = item.UnitOfMeasure;
                sheet.Cell(currentRow, 5).Value = item.PurchasePrice;
                sheet.Cell(currentRow, 6).Value = item.CostPrice;
                sheet.Cell(currentRow, 7).Value = item.OpeningStockQuantity;
                sheet.Cell(currentRow, 8).Value = item.OpeningStockValue;
                sheet.Cell(currentRow, 9).Value = item.PeriodQuantityIn;
                sheet.Cell(currentRow, 10).Value = item.PeriodValueIn;
                sheet.Cell(currentRow, 11).Value = item.PeriodQuantityOut;
                sheet.Cell(currentRow, 12).Value = item.PeriodValueOut;
                sheet.Cell(currentRow, 13).Value = item.ClosingStockQuantity;
                sheet.Cell(currentRow, 14).Value = item.ClosingStockValue;

                // Định dạng số (tương tự như code trước)
                sheet.Cell(currentRow, 5).Style.NumberFormat.Format = item.PurchasePrice.HasValue && item.PurchasePrice != 0 ? "#,##0" : "-";
                sheet.Cell(currentRow, 6).Style.NumberFormat.Format = item.CostPrice != 0 ? "#,##0" : "-";
                Action<int, decimal> formatQuantityCell = (col, val) => sheet.Cell(currentRow, col).Style.NumberFormat.Format = val != 0 ? "#,##0.###" : "-";
                Action<int, decimal> formatValueCell = (col, val) => sheet.Cell(currentRow, col).Style.NumberFormat.Format = val != 0 ? "#,##0" : "-";

                formatQuantityCell(7, item.OpeningStockQuantity); formatValueCell(8, item.OpeningStockValue);
                formatQuantityCell(9, item.PeriodQuantityIn); formatValueCell(10, item.PeriodValueIn);
                formatQuantityCell(11, item.PeriodQuantityOut); formatValueCell(12, item.PeriodValueOut);
                formatQuantityCell(13, item.ClosingStockQuantity); formatValueCell(14, item.ClosingStockValue);

                sheet.Range(currentRow, 1, currentRow, headers.Length).Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin).Border.SetInsideBorder(XLBorderStyleValues.Thin);
                currentRow++;
            }
            sheet.Columns().AdjustToContents(2, 75);
        }

        // Hàm helper để render Sheet Chi Tiết cho từng NVL
        private void RenderDetailSheetForItem(IXLWorksheet sheet, InventoryDetailReportItemVM itemData)
        {
            int currentRow = 1;
            // (Code render Sheet Chi Tiết như trong phiên bản gốc của bạn hoặc phiên bản đã điều chỉnh 1 sheet/NVL)
            // ... Bắt đầu từ thông tin chung của báo cáo cho NVL này ...
            sheet.Cell(currentRow, 1).Value = $"CHI TIẾT TỒN KHO NGUYÊN VẬT LIỆU";
            sheet.Range(currentRow, 1, currentRow, 13).Merge().Style.Font.SetBold().Font.SetFontSize(14).Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
            currentRow++;
            sheet.Cell(currentRow, 1).Value = $"Nguyên vật liệu: {itemData.IngredientName} ({itemData.IngredientCode})";
            sheet.Range(currentRow, 1, currentRow, 5).Merge().Style.Font.SetBold();
            sheet.Cell(currentRow, 7).Value = $"ĐVT: {itemData.UnitOfMeasure}";
            sheet.Cell(currentRow, 10).Value = $"Tồn TT: {(itemData.MinimumStockLevel.HasValue ? itemData.MinimumStockLevel.Value.ToString("#,##0.###") : "-")}";
            currentRow++;
            sheet.Cell(currentRow, 1).Value = $"Kỳ báo cáo: {itemData.PeriodStartDate:dd/MM/yyyy} - {itemData.PeriodEndDate:dd/MM/yyyy}";
            sheet.Range(currentRow, 1, currentRow, 13).Merge();
            currentRow += 2;

            // Tiêu đề bảng chi tiết
            var headers = new[] { "Ngày GD", "Số Phiếu", "Loại GD/Diễn giải", "Người TH", "NCC", "SL Nhập", "ĐG Nhập", "TT Nhập", "SL Xuất", "ĐG Xuất", "TT Xuất", "SL Tồn", "Ghi Chú" };
            for (int i = 0; i < headers.Length; i++)
            {
                sheet.Cell(currentRow, i + 1).Value = headers[i];
            }
            sheet.Range(currentRow, 1, currentRow, headers.Length).Style.Font.SetBold().Fill.SetBackgroundColor(XLColor.LightGray).Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
            currentRow++;

            // Tồn đầu kỳ
            sheet.Cell(currentRow, 1).Value = itemData.PeriodStartDate.AddDays(-1).ToString("dd/MM/yyyy");
            sheet.Cell(currentRow, 3).Value = "Tồn đầu kỳ";
            sheet.Cell(currentRow, 7).Value = itemData.OpeningStockUnitPrice; // ĐG
            sheet.Cell(currentRow, 7).Style.NumberFormat.Format = itemData.OpeningStockUnitPrice != 0 ? "#,##0" : "-";
            sheet.Cell(currentRow, 8).Value = itemData.OpeningStockValue;   // TT
            sheet.Cell(currentRow, 8).Style.NumberFormat.Format = itemData.OpeningStockValue != 0 ? "#,##0" : "-";
            sheet.Cell(currentRow, 12).Value = itemData.OpeningStockQuantity; // SL Tồn
            sheet.Cell(currentRow, 12).Style.NumberFormat.Format = itemData.OpeningStockQuantity != 0 ? "#,##0.###" : "-";
            sheet.Range(currentRow, 1, currentRow, headers.Length).Style.Font.SetItalic();
            currentRow++;

            // Dữ liệu giao dịch
            foreach (var trans in itemData.Transactions)
            {
                sheet.Cell(currentRow, 1).Value = trans.TransactionDate.ToString("dd/MM/yyyy HH:mm");
                sheet.Cell(currentRow, 2).Value = trans.TransactionId;
                sheet.Cell(currentRow, 3).Value = trans.TransactionTypeDisplay;
                sheet.Cell(currentRow, 4).Value = trans.UserName;
                sheet.Cell(currentRow, 5).Value = trans.SupplierName;
                sheet.Cell(currentRow, 6).Value = trans.QuantityIn;
                sheet.Cell(currentRow, 7).Value = trans.UnitPriceIn;
                sheet.Cell(currentRow, 8).Value = trans.ValueIn;
                sheet.Cell(currentRow, 9).Value = trans.QuantityOut;
                sheet.Cell(currentRow, 10).Value = trans.UnitPriceOut;
                sheet.Cell(currentRow, 11).Value = trans.ValueOut;
                sheet.Cell(currentRow, 12).Value = trans.StockAfterTransaction;
                sheet.Cell(currentRow, 13).Value = trans.Notes;
                // Định dạng số
                Action<int, decimal?> formatNullableQuantity = (col, val) => sheet.Cell(currentRow, col).Style.NumberFormat.Format = val.HasValue && val != 0 ? "#,##0.###" : "-";
                Action<int, decimal?> formatNullableValue = (col, val) => sheet.Cell(currentRow, col).Style.NumberFormat.Format = val.HasValue && val != 0 ? "#,##0" : "-";
                formatNullableQuantity(6, trans.QuantityIn); formatNullableValue(7, trans.UnitPriceIn); formatNullableValue(8, trans.ValueIn);
                formatNullableQuantity(9, trans.QuantityOut); formatNullableValue(10, trans.UnitPriceOut); formatNullableValue(11, trans.ValueOut);
                sheet.Cell(currentRow, 12).Style.NumberFormat.Format = trans.StockAfterTransaction != 0 ? "#,##0.###" : "-";
                currentRow++;
            }

            // Tổng cộng phát sinh
            sheet.Cell(currentRow, 3).Value = "Tổng cộng phát sinh";
            sheet.Cell(currentRow, 6).Value = itemData.TotalQuantityIn;
            sheet.Cell(currentRow, 6).Style.NumberFormat.Format = itemData.TotalQuantityIn != 0 ? "#,##0.###" : "-";
            sheet.Cell(currentRow, 8).Value = itemData.TotalValueIn;
            sheet.Cell(currentRow, 8).Style.NumberFormat.Format = itemData.TotalValueIn != 0 ? "#,##0" : "-";
            sheet.Cell(currentRow, 9).Value = itemData.TotalQuantityOut;
            sheet.Cell(currentRow, 9).Style.NumberFormat.Format = itemData.TotalQuantityOut != 0 ? "#,##0.###" : "-";
            sheet.Cell(currentRow, 11).Value = itemData.TotalValueOut;
            sheet.Cell(currentRow, 11).Style.NumberFormat.Format = itemData.TotalValueOut != 0 ? "#,##0" : "-";
            sheet.Range(currentRow, 1, currentRow, headers.Length).Style.Font.SetBold().Fill.SetBackgroundColor(XLColor.LightYellow);
            currentRow++;

            // Tồn cuối kỳ
            sheet.Cell(currentRow, 1).Value = itemData.PeriodEndDate.ToString("dd/MM/yyyy");
            sheet.Cell(currentRow, 3).Value = "Tồn cuối kỳ";
            sheet.Cell(currentRow, 10).Value = itemData.ClosingStockUnitPrice; // ĐG
            sheet.Cell(currentRow, 10).Style.NumberFormat.Format = itemData.ClosingStockUnitPrice != 0 ? "#,##0" : "-";
            sheet.Cell(currentRow, 11).Value = itemData.ClosingStockValue;   // TT
            sheet.Cell(currentRow, 11).Style.NumberFormat.Format = itemData.ClosingStockValue != 0 ? "#,##0" : "-";
            sheet.Cell(currentRow, 12).Value = itemData.ClosingStockQuantity; // SL Tồn
            sheet.Cell(currentRow, 12).Style.NumberFormat.Format = itemData.ClosingStockQuantity != 0 ? "#,##0.###" : "-";
            sheet.Range(currentRow, 1, currentRow, headers.Length).Style.Font.SetBold().Font.SetItalic();
            currentRow++;

            // Số lượng cần nhập thêm
            if (itemData.QuantityToOrder.HasValue && itemData.QuantityToOrder.Value > 0)
            {
                sheet.Cell(currentRow, 3).Value = "Số lượng cần nhập thêm (dự kiến):";
                sheet.Cell(currentRow, 6).Value = itemData.QuantityToOrder;
                sheet.Cell(currentRow, 6).Style.NumberFormat.Format = itemData.QuantityToOrder.Value != 0 ? "#,##0.###" : "-";
                sheet.Range(currentRow, 3, currentRow, 6).Style.Font.SetBold().Font.SetFontColor(XLColor.Red);
                // currentRow++; // Không cần tăng vì đây là dòng cuối cùng của block này
            }
            sheet.Columns().AdjustToContents(2, 75);
        }

        #endregion

        #region Nguyên vật liệu (Ingredients) - Giữ nguyên các action hiện có của bạn

        // POST: /Admin/Inventory/GetIngredients
        [HttpPost]
        public async Task<IActionResult> GetIngredients()
        {
            try
            {
                var draw = Request.Form["draw"].FirstOrDefault();
                var start = Request.Form["start"].FirstOrDefault();
                var length = Request.Form["length"].FirstOrDefault();
                var orderColumnIndex = Request.Form["order[0][column]"].FirstOrDefault();
                var sortColumn = Request.Form[$"columns[{orderColumnIndex}][name]"].FirstOrDefault()?.ToLower();
                var sortColumnDirection = Request.Form["order[0][dir]"].FirstOrDefault();
                var searchValue = Request.Form["search[value]"].FirstOrDefault()?.ToLower() ?? "";

                int pageSize = (int.TryParse(length, out int len) && len > 0) ? len : 10;
                if (pageSize == -1) pageSize = await _context.Ingredients.CountAsync(i => !i.DeleteTemp);
                int skip = (int.TryParse(start, out int st)) ? st : 0;

                var query = _context.Ingredients.Where(i => !i.DeleteTemp).AsQueryable();

                if (!string.IsNullOrEmpty(searchValue))
                {
                    query = query.Where(i =>
                        (i.Name != null && i.Name.ToLower().Contains(searchValue)) ||
                        (i.UnitOfMeasure != null && i.UnitOfMeasure.ToLower().Contains(searchValue)) ||
                        (i.Description != null && i.Description.ToLower().Contains(searchValue))
                    );
                }

                int recordsFiltered = await query.CountAsync();

                if (!string.IsNullOrEmpty(sortColumn) && !string.IsNullOrEmpty(sortColumnDirection))
                {
                    Expression<Func<Ingredient, object>> keySelector = sortColumn switch
                    {
                        "name" => i => i.Name,
                        "unitofmeasure" => i => i.UnitOfMeasure,
                        "currentstocklevel" => i => i.CurrentStockLevel,
                        "minimumstocklevel" => i => i.MinimumStockLevel ?? 0, // Sửa lỗi tiềm ẩn nếu là null
                        "lastpurchaseprice" => i => i.LastPurchasePrice,
                        "isactive" => i => i.IsActive,
                        "updatedat" => i => i.UpdatedAt,
                        _ => i => i.Id // Mặc định sắp xếp theo Id
                    };

                    query = sortColumnDirection.Equals("desc", StringComparison.OrdinalIgnoreCase)
                        ? query.OrderByDescending(keySelector)
                        : query.OrderBy(keySelector);
                }
                else
                {
                    query = query.OrderByDescending(i => i.UpdatedAt);
                }

                var pagedData = await query.Skip(skip).Take(pageSize).ToListAsync();

                var viewModelData = pagedData.Select(i => new
                {
                    i.Id,
                    i.Name,
                    Description = i.Description ?? "",
                    i.UnitOfMeasure,
                    i.CurrentStockLevel,
                    i.MinimumStockLevel,
                    i.LastPurchasePrice,
                    i.IsActive,
                    UpdatedAt = i.UpdatedAt.ToString("dd/MM/yyyy HH:mm"),
                    i.DeleteTemp // Giữ lại để JS có thể xử lý logic ẩn nếu cần
                }).ToList();

                int recordsTotal = await _context.Ingredients.CountAsync(i => !i.DeleteTemp);
                var jsonData = new { draw, recordsFiltered, recordsTotal, data = viewModelData };
                return Ok(jsonData);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi lấy danh sách nguyên vật liệu.");
                return StatusCode(500, new { error = "Lỗi máy chủ khi lấy danh sách nguyên vật liệu." });
            }
        }

        // GET: /Admin/Inventory/GetIngredient/5
        [HttpGet]
        public async Task<IActionResult> GetIngredient(int id)
        {
            if (id <= 0) return BadRequest(new { success = false, message = "ID nguyên vật liệu không hợp lệ." });
            var ingredient = await _context.Ingredients.FirstOrDefaultAsync(i => i.Id == id && !i.DeleteTemp);
            if (ingredient == null) return NotFound(new { success = false, message = "Không tìm thấy nguyên vật liệu." });

            return Ok(new
            {
                success = true,
                data = new
                {
                    ingredient.Id,
                    ingredient.Name,
                    ingredient.Description,
                    ingredient.UnitOfMeasure,
                    ingredient.CurrentStockLevel,
                    ingredient.MinimumStockLevel,
                    ingredient.LastPurchasePrice,
                    ingredient.IsActive,
                }
            });
        }

        // POST: /Admin/Inventory/CreateIngredient
        [HttpPost]
        public async Task<IActionResult> CreateIngredient([FromBody] Ingredient model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(new { success = false, errors = ModelState.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Errors.Select(e => e.ErrorMessage).ToArray()) });
            }

            var normalizedName = model.Name.Trim().ToLower();
            if (await _context.Ingredients.AnyAsync(s => !s.DeleteTemp && s.Name.Trim().ToLower() == normalizedName))
            {
                return BadRequest(new { success = false, message = $"Tên nguyên vật liệu '{model.Name}' đã tồn tại." });
            }

            model.CreatedAt = DateTime.Now;
            model.UpdatedAt = DateTime.Now;
            model.DeleteTemp = false;

            _context.Ingredients.Add(model);
            await _context.SaveChangesAsync();
            return Ok(new { success = true, message = "Thêm nguyên vật liệu thành công!", data = model });
        }

        // POST: /Admin/Inventory/UpdateIngredient
        [HttpPost]
        public async Task<IActionResult> UpdateIngredient([FromBody] Ingredient model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(new { success = false, errors = ModelState.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Errors.Select(e => e.ErrorMessage).ToArray()) });
            }

            var existingIngredient = await _context.Ingredients.FirstOrDefaultAsync(i => i.Id == model.Id && !i.DeleteTemp);
            if (existingIngredient == null)
            {
                return NotFound(new { success = false, message = "Không tìm thấy nguyên vật liệu." });
            }

            var normalizedName = model.Name.Trim().ToLower();
            if (await _context.Ingredients.AnyAsync(s => s.Id != model.Id && !s.DeleteTemp && s.Name.Trim().ToLower() == normalizedName))
            {
                return BadRequest(new { success = false, message = $"Tên nguyên vật liệu '{model.Name}' đã tồn tại." });
            }

            existingIngredient.Name = model.Name;
            existingIngredient.Description = model.Description;
            existingIngredient.UnitOfMeasure = model.UnitOfMeasure;
            existingIngredient.MinimumStockLevel = model.MinimumStockLevel;
            existingIngredient.IsActive = model.IsActive;
            existingIngredient.UpdatedAt = DateTime.Now;

            await _context.SaveChangesAsync();
            return Ok(new { success = true, message = "Cập nhật nguyên vật liệu thành công!", data = existingIngredient });
        }

        // POST: /Admin/Inventory/DeleteIngredient/5
        [HttpPost]
        public async Task<IActionResult> DeleteIngredient(int id)
        {
            var ingredient = await _context.Ingredients.FirstOrDefaultAsync(i => i.Id == id && !i.DeleteTemp);
            if (ingredient == null)
            {
                return NotFound(new { success = false, message = "Không tìm thấy nguyên vật liệu." });
            }

            bool isInProductIngredient = await _context.ProductIngredients.AnyAsync(pi => pi.IngredientId == id);
            bool isInTransaction = await _context.InventoryTransactions.AnyAsync(it => it.IngredientId == id);

            if (isInProductIngredient)
            {
                return BadRequest(new { success = false, message = "Không thể xóa nguyên vật liệu vì đang được sử dụng trong công thức sản phẩm." });
            }
            if (isInTransaction)
            {
                // Thay vì chặn xóa, có thể cho phép xóa mềm nhưng cảnh báo
                // return BadRequest(new { success = false, message = "Không thể xóa nguyên vật liệu vì đã có giao dịch kho liên quan. Hãy cân nhắc vô hiệu hóa." });
            }

            ingredient.DeleteTemp = true;
            ingredient.IsActive = false;
            ingredient.UpdatedAt = DateTime.Now;
            await _context.SaveChangesAsync();
            return Ok(new { success = true, message = "Xóa tạm nguyên vật liệu thành công." });
        }

        // POST: /Admin/Inventory/ToggleIngredientActive/5
        [HttpPost]
        public async Task<IActionResult> ToggleIngredientActive(int id)
        {
            var ingredient = await _context.Ingredients.FirstOrDefaultAsync(i => i.Id == id && !i.DeleteTemp);
            if (ingredient == null)
            {
                return NotFound(new { success = false, message = "Không tìm thấy nguyên vật liệu." });
            }
            ingredient.IsActive = !ingredient.IsActive;
            ingredient.UpdatedAt = DateTime.Now;
            await _context.SaveChangesAsync();
            return Ok(new { success = true, message = $"Đã {(ingredient.IsActive ? "kích hoạt" : "vô hiệu hóa")} nguyên vật liệu.", isActive = ingredient.IsActive });
        }

        #endregion

        #region Nhà cung cấp (Suppliers) - Giữ nguyên
        // POST: /Admin/Inventory/GetSuppliers
        [HttpPost]
        public async Task<IActionResult> GetSuppliers()
        {
            try
            {
                var draw = Request.Form["draw"].FirstOrDefault();
                var start = Request.Form["start"].FirstOrDefault();
                var length = Request.Form["length"].FirstOrDefault();
                var orderColumnIndex = Request.Form["order[0][column]"].FirstOrDefault();
                var sortColumn = Request.Form[$"columns[{orderColumnIndex}][name]"].FirstOrDefault()?.ToLower();
                var sortColumnDirection = Request.Form["order[0][dir]"].FirstOrDefault();
                var searchValue = Request.Form["search[value]"].FirstOrDefault()?.ToLower() ?? "";

                int pageSize = (int.TryParse(length, out int len) && len > 0) ? len : 10;
                if (pageSize == -1) pageSize = await _context.Suppliers.CountAsync(s => !s.DeleteTemp);
                int skip = (int.TryParse(start, out int st)) ? st : 0;

                var query = _context.Suppliers.Where(s => !s.DeleteTemp).AsQueryable();

                if (!string.IsNullOrEmpty(searchValue))
                {
                    query = query.Where(s =>
                        (s.Name != null && s.Name.ToLower().Contains(searchValue)) ||
                        (s.ContactPerson != null && s.ContactPerson.ToLower().Contains(searchValue)) ||
                        (s.PhoneNumber != null && s.PhoneNumber.Contains(searchValue)) ||
                        (s.Email != null && s.Email.ToLower().Contains(searchValue)) ||
                        (s.Address != null && s.Address.ToLower().Contains(searchValue))
                    );
                }

                int recordsFiltered = await query.CountAsync();

                if (!string.IsNullOrEmpty(sortColumn) && !string.IsNullOrEmpty(sortColumnDirection))
                {
                    Expression<Func<Supplier, object>> keySelector = sortColumn switch
                    {
                        "name" => s => s.Name,
                        "contactperson" => s => s.ContactPerson,
                        "phonenumber" => s => s.PhoneNumber,
                        "email" => s => s.Email,
                        "updatedat" => s => s.UpdatedAt,
                        _ => s => s.Id
                    };
                    query = sortColumnDirection.Equals("desc", StringComparison.OrdinalIgnoreCase)
                        ? query.OrderByDescending(keySelector)
                        : query.OrderBy(keySelector);
                }
                else
                {
                    query = query.OrderByDescending(s => s.UpdatedAt);
                }

                var pagedData = await query.Skip(skip).Take(pageSize).ToListAsync();
                var viewModelData = pagedData.Select(s => new
                {
                    s.Id,
                    s.Name,
                    ContactPerson = s.ContactPerson ?? "",
                    PhoneNumber = s.PhoneNumber ?? "",
                    Email = s.Email ?? "",
                    Address = s.Address ?? "",
                    Notes = s.Notes ?? "",
                    UpdatedAt = s.UpdatedAt.ToString("dd/MM/yyyy HH:mm"),
                    s.DeleteTemp
                }).ToList();

                int recordsTotal = await _context.Suppliers.CountAsync(s => !s.DeleteTemp);
                var jsonData = new { draw, recordsFiltered, recordsTotal, data = viewModelData };
                return Ok(jsonData);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi lấy danh sách nhà cung cấp.");
                return StatusCode(500, new { error = "Lỗi máy chủ khi lấy danh sách nhà cung cấp." });
            }
        }

        // GET: /Admin/Inventory/GetSupplier/5
        [HttpGet]
        public async Task<IActionResult> GetSupplier(int id)
        {
            if (id <= 0) return BadRequest(new { success = false, message = "ID nhà cung cấp không hợp lệ." });
            var supplier = await _context.Suppliers.FirstOrDefaultAsync(s => s.Id == id && !s.DeleteTemp);
            if (supplier == null) return NotFound(new { success = false, message = "Không tìm thấy nhà cung cấp." });
            return Ok(new { success = true, data = supplier });
        }

        // POST: /Admin/Inventory/CreateSupplier
        [HttpPost]
        public async Task<IActionResult> CreateSupplier([FromBody] Supplier model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(new { success = false, errors = ModelState.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Errors.Select(e => e.ErrorMessage).ToArray()) });
            }

            var normalizedName = model.Name.Trim().ToLower();
            if (await _context.Suppliers.AnyAsync(s => !s.DeleteTemp && s.Name.Trim().ToLower() == normalizedName))
            {
                return BadRequest(new { success = false, message = $"Tên nhà cung cấp '{model.Name}' đã tồn tại." });
            }

            model.CreatedAt = DateTime.Now;
            model.UpdatedAt = DateTime.Now;
            model.DeleteTemp = false;

            _context.Suppliers.Add(model);
            await _context.SaveChangesAsync();
            return Ok(new { success = true, message = "Thêm nhà cung cấp thành công!", data = model });
        }

        // POST: /Admin/Inventory/UpdateSupplier
        [HttpPost]
        public async Task<IActionResult> UpdateSupplier([FromBody] Supplier model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(new { success = false, errors = ModelState.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Errors.Select(e => e.ErrorMessage).ToArray()) });
            }

            var existingSupplier = await _context.Suppliers.FirstOrDefaultAsync(s => s.Id == model.Id && !s.DeleteTemp);
            if (existingSupplier == null)
            {
                return NotFound(new { success = false, message = "Không tìm thấy nhà cung cấp." });
            }
            var normalizedName = model.Name.Trim().ToLower();
            if (await _context.Suppliers.AnyAsync(s => s.Id != model.Id && !s.DeleteTemp && s.Name.Trim().ToLower() == normalizedName))
            {
                return BadRequest(new { success = false, message = $"Tên nhà cung cấp '{model.Name}' đã tồn tại." });
            }

            existingSupplier.Name = model.Name;
            existingSupplier.ContactPerson = model.ContactPerson;
            existingSupplier.PhoneNumber = model.PhoneNumber;
            existingSupplier.Email = model.Email;
            existingSupplier.Address = model.Address;
            existingSupplier.Notes = model.Notes;
            existingSupplier.UpdatedAt = DateTime.Now;

            await _context.SaveChangesAsync();
            return Ok(new { success = true, message = "Cập nhật nhà cung cấp thành công!", data = existingSupplier });
        }

        // POST: /Admin/Inventory/DeleteSupplier/5
        [HttpPost]
        public async Task<IActionResult> DeleteSupplier(int id)
        {
            var supplier = await _context.Suppliers.FirstOrDefaultAsync(s => s.Id == id && !s.DeleteTemp);
            if (supplier == null)
            {
                return NotFound(new { success = false, message = "Không tìm thấy nhà cung cấp." });
            }

            if (await _context.InventoryTransactions.AnyAsync(it => it.SupplierId == id))
            {
                return BadRequest(new { success = false, message = "Không thể xóa nhà cung cấp vì đã có giao dịch kho liên quan." });
            }

            supplier.DeleteTemp = true;
            supplier.UpdatedAt = DateTime.Now;
            await _context.SaveChangesAsync();
            return Ok(new { success = true, message = "Xóa tạm nhà cung cấp thành công." });
        }
        #endregion

        #region Giao dịch Kho (Inventory Transactions) - Giữ nguyên và bổ sung hàm GetTransactionTypeDisplayName nếu chưa có ngoài controller

        private string GetTransactionTypeDisplayName(InventoryTransactionType type)
        {
            return type switch
            {
                InventoryTransactionType.InitialStock => "Tồn kho ban đầu",
                InventoryTransactionType.Purchase => "Nhập mua hàng",
                InventoryTransactionType.SaleConsumption => "Xuất bán hàng",
                InventoryTransactionType.AdjustmentIn => "Điều chỉnh tăng",
                InventoryTransactionType.AdjustmentOut => "Điều chỉnh giảm",
                _ => type.ToString()
            };
        }


        // POST: /Admin/Inventory/CreateInventoryTransaction
        [HttpPost]
        public async Task<IActionResult> CreateInventoryTransaction([FromBody] InventoryTransactionVM model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(new { success = false, errors = ModelState.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Errors.Select(e => e.ErrorMessage).ToArray()) });
            }

            var ingredient = await _context.Ingredients.FirstOrDefaultAsync(i => i.Id == model.IngredientId && !i.DeleteTemp && i.IsActive);
            if (ingredient == null)
            {
                return BadRequest(new { success = false, message = "Nguyên vật liệu không hợp lệ hoặc đã bị vô hiệu hóa." });
            }

            if ((model.TransactionType == InventoryTransactionType.Purchase || model.TransactionType == InventoryTransactionType.InitialStock) && !model.UnitPrice.HasValue)
            {
                ModelState.AddModelError("UnitPrice", "Đơn giá là bắt buộc cho loại giao dịch này.");
            }
            if (model.TransactionType == InventoryTransactionType.Purchase && !model.SupplierId.HasValue)
            {
                ModelState.AddModelError("SupplierId", "Nhà cung cấp là bắt buộc cho loại giao dịch mua hàng.");
            }
            if (!ModelState.IsValid)
            {
                return BadRequest(new { success = false, errors = ModelState.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Errors.Select(e => e.ErrorMessage).ToArray()) });
            }

            var currentUser = await _userManager.GetUserAsync(User);
            if (currentUser == null)
            {
                return Unauthorized(new { success = false, message = "Yêu cầu đăng nhập." });
            }

            var transaction = new InventoryTransaction
            {
                IngredientId = model.IngredientId,
                TransactionType = model.TransactionType,
                TransactionDate = model.TransactionDate == DateTime.MinValue ? DateTime.Now : model.TransactionDate,
                QuantityChanged = model.Quantity, // Sẽ điều chỉnh dấu ở dưới
                UnitPrice = model.UnitPrice,
                SupplierId = model.TransactionType == InventoryTransactionType.Purchase ? model.SupplierId : null,
                UserId = currentUser.Id,
                Notes = model.Notes,
                CreatedAt = DateTime.Now
            };

            // Tính TotalPrice
            if (transaction.UnitPrice.HasValue)
            {
                // TotalPrice sẽ mang dấu của QuantityChanged (âm nếu là xuất có giá trị)
                // Tuy nhiên, ở đây QuantityChanged đang là số dương từ model.Quantity
                // Chúng ta sẽ tính TotalPrice dựa trên model.Quantity (luôn dương) và UnitPrice
                // Dấu của TotalPrice sẽ được xử lý sau khi xác định QuantityChanged cuối cùng
                transaction.TotalPrice = model.Quantity * transaction.UnitPrice.Value;
            }


            switch (model.TransactionType)
            {
                case InventoryTransactionType.InitialStock:
                case InventoryTransactionType.Purchase:
                case InventoryTransactionType.AdjustmentIn:
                    ingredient.CurrentStockLevel += model.Quantity;
                    if (model.TransactionType == InventoryTransactionType.Purchase && model.UnitPrice.HasValue)
                    {
                        ingredient.LastPurchasePrice = model.UnitPrice.Value;
                    }
                    // transaction.QuantityChanged giữ nguyên dương
                    // transaction.TotalPrice giữ nguyên dương
                    break;

                case InventoryTransactionType.SaleConsumption:
                case InventoryTransactionType.AdjustmentOut:
                    if (ingredient.CurrentStockLevel < model.Quantity && model.TransactionType != InventoryTransactionType.AdjustmentOut /*Cho phép điều chỉnh âm tồn kho nếu là AdjustmentOut*/)
                    {
                        return BadRequest(new { success = false, message = $"Không đủ tồn kho cho '{ingredient.Name}'. Tồn kho hiện tại: {ingredient.CurrentStockLevel.ToString("N3", _vnCulture)} {ingredient.UnitOfMeasure}." });
                    }
                    transaction.QuantityChanged = -model.Quantity; // Đổi thành số âm
                    ingredient.CurrentStockLevel -= model.Quantity;

                    // TotalPrice sẽ là số âm nếu QuantityChanged là âm
                    if (transaction.TotalPrice.HasValue)
                    {
                        transaction.TotalPrice = -transaction.TotalPrice.Value;
                    }
                    // Nếu là xuất bán và UnitPrice không được cung cấp (dựa vào TotalPrice tính từ LastPurchasePrice), TotalPrice cũng phải là âm
                    else if (model.TransactionType == InventoryTransactionType.SaleConsumption)
                    {
                        // Logic này cần xem xét lại, vì UnitPrice của SaleConsumption có thể được ẩn và TotalPrice được tính theo LastPurchasePrice
                        // Nếu UnitPrice được nhập cho SaleConsumption (là giá vốn), thì TotalPrice ở trên đã đúng
                        // Nếu UnitPrice không được nhập, và TotalPrice được tính sẵn từ LastPurchasePrice (phía client chẳng hạn)
                        // thì TotalPrice đó cần được truyền vào model.
                        // Trong logic hiện tại của controller này, nếu UnitPrice không có, TotalPrice sẽ không được tính lại dựa trên LastPurchasePrice.
                        // Ta cần đảm bảo rằng model.TotalPrice (nếu có) được truyền và sử dụng, hoặc tính toán lại.
                        // Giả sử model.TotalPrice đã được tính toán đúng (dương) ở client
                        if (model.TotalPrice.HasValue) transaction.TotalPrice = -model.TotalPrice.Value;
                        else if (model.UnitPrice.HasValue) transaction.TotalPrice = -(model.Quantity * model.UnitPrice.Value); // Đã tính ở trên
                        else // Fallback nếu không có UnitPrice và TotalPrice từ model.
                        {
                            transaction.UnitPrice = ingredient.LastPurchasePrice; // Ghi nhận giá vốn xuất
                            transaction.TotalPrice = transaction.QuantityChanged * ingredient.LastPurchasePrice; // Sẽ là số âm
                        }
                    }
                    break;
            }

            ingredient.UpdatedAt = DateTime.Now; // Cập nhật ngày thay đổi của NVL
            _context.InventoryTransactions.Add(transaction);
            _context.Entry(ingredient).State = EntityState.Modified;

            await _context.SaveChangesAsync();

            return Ok(new { success = true, message = "Tạo phiếu kho thành công!", data = transaction });
        }


        // POST: /Admin/Inventory/GetInventoryTransactions
        [HttpPost]
        public async Task<IActionResult> GetInventoryTransactions()
        {
            try
            {
                var draw = Request.Form["draw"].FirstOrDefault();
                var start = Request.Form["start"].FirstOrDefault();
                var length = Request.Form["length"].FirstOrDefault();
                var orderColumnIndex = Request.Form["order[0][column]"].FirstOrDefault();
                var sortColumn = Request.Form[$"columns[{orderColumnIndex}][name]"].FirstOrDefault()?.ToLower();
                var sortColumnDirection = Request.Form["order[0][dir]"].FirstOrDefault();

                var fromDateStr = Request.Form["fromDate"].FirstOrDefault();
                var toDateStr = Request.Form["toDate"].FirstOrDefault();
                var transactionTypeFilterStr = Request.Form["transactionTypeFilter"].FirstOrDefault();
                var ingredientFilterIdStr = Request.Form["ingredientFilterId"].FirstOrDefault();
                var generalSearchValue = Request.Form["search[value]"].FirstOrDefault()?.ToLower() ?? "";

                int pageSize = (int.TryParse(length, out int len) && len > 0) ? len : 10;
                if (pageSize == -1) pageSize = await _context.InventoryTransactions.CountAsync();
                int skip = (int.TryParse(start, out int st)) ? st : 0;

                var query = _context.InventoryTransactions
                                      .Include(it => it.Ingredient)
                                      .Include(it => it.Supplier)
                                      .Include(it => it.User)
                                      .AsQueryable();

                if (DateTime.TryParseExact(fromDateStr, "d/M/yyyy", _vnCulture, DateTimeStyles.None, out var fromDate))
                { query = query.Where(it => it.TransactionDate >= fromDate.Date); } // Lấy từ đầu ngày
                if (DateTime.TryParseExact(toDateStr, "d/M/yyyy", _vnCulture, DateTimeStyles.None, out var toDate))
                { query = query.Where(it => it.TransactionDate < toDate.Date.AddDays(1)); } // Lấy đến cuối ngày

                if (Enum.TryParse<InventoryTransactionType>(transactionTypeFilterStr, out var transactionType))
                { query = query.Where(it => it.TransactionType == transactionType); }

                if (int.TryParse(ingredientFilterIdStr, out int ingredientId) && ingredientId > 0)
                { query = query.Where(it => it.IngredientId == ingredientId); }

                if (!string.IsNullOrEmpty(generalSearchValue))
                {
                    query = query.Where(it =>
                        (it.Ingredient.Name != null && it.Ingredient.Name.ToLower().Contains(generalSearchValue)) ||
                        (it.Supplier.Name != null && it.Supplier.Name.ToLower().Contains(generalSearchValue)) ||
                        (it.User.Name != null && it.User.Name.ToLower().Contains(generalSearchValue)) ||
                        (it.Notes != null && it.Notes.ToLower().Contains(generalSearchValue)) ||
                        (it.Id.ToString().Contains(generalSearchValue)) // Tìm theo số phiếu
                    );
                }

                int recordsFiltered = await query.CountAsync();

                if (!string.IsNullOrEmpty(sortColumn) && !string.IsNullOrEmpty(sortColumnDirection))
                {
                    Expression<Func<InventoryTransaction, object>> keySelector = sortColumn switch
                    {
                        "transactiondate" => it => it.TransactionDate,
                        "ingredientname" => it => it.Ingredient.Name, // Sắp xếp theo tên NVL
                        "transactiontype" => it => it.TransactionType,
                        "quantitychanged" => it => it.QuantityChanged,
                        "unitprice" => it => it.UnitPrice ?? 0,
                        "totalprice" => it => it.TotalPrice ?? 0,
                        "username" => it => it.User.Name, // Sắp xếp theo tên người dùng
                        _ => it => it.Id
                    };
                    query = sortColumnDirection.Equals("desc", StringComparison.OrdinalIgnoreCase)
                        ? query.OrderByDescending(keySelector)
                        : query.OrderBy(keySelector);
                }
                else
                {
                    query = query.OrderByDescending(it => it.TransactionDate).ThenByDescending(it => it.Id);
                }

                var pagedData = await query.Skip(skip).Take(pageSize).ToListAsync();
                var viewModelData = pagedData.Select(it => new
                {
                    it.Id,
                    TransactionDate = it.TransactionDate.ToString("dd/MM/yyyy HH:mm"),
                    IngredientName = it.Ingredient?.Name ?? "N/A",
                    IngredientUnit = it.Ingredient?.UnitOfMeasure ?? "",
                    TransactionType = it.TransactionType.ToString(),
                    TransactionTypeDisplay = GetTransactionTypeDisplayName(it.TransactionType),
                    it.QuantityChanged,
                    UnitPrice = it.UnitPrice, // Gửi số thô
                    TotalPrice = it.TotalPrice, // Gửi số thô
                    SupplierName = it.Supplier?.Name,
                    UserName = it.User?.Name ?? (it.UserId == null ? "Hệ thống" : "N/A"),
                    Notes = it.Notes ?? ""
                }).ToList();

                int recordsTotal = await _context.InventoryTransactions.CountAsync();
                var jsonData = new { draw, recordsFiltered, recordsTotal, data = viewModelData };
                return Ok(jsonData);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi lấy lịch sử giao dịch kho.");
                return StatusCode(500, new { error = "Lỗi máy chủ khi lấy lịch sử giao dịch kho." });
            }
        }


        #endregion

        #region Helper Actions for Dropdowns - Giữ nguyên
        // GET: /Admin/Inventory/GetIngredientsForDropdown
        [HttpGet]
        public async Task<IActionResult> GetIngredientsForDropdown(string searchTerm)
        {
            var query = _context.Ingredients
                                  .Where(i => !i.DeleteTemp && i.IsActive);

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                query = query.Where(i => i.Name.Contains(searchTerm));
            }

            var ingredients = await query
                                    .OrderBy(i => i.Name)
                                    .Take(20) // Giới hạn số lượng kết quả
                                    .Select(i => new { id = i.Id, text = i.Name, unit = i.UnitOfMeasure, currentStock = i.CurrentStockLevel })
                                    .ToListAsync();
            return Ok(ingredients);
        }

        // GET: /Admin/Inventory/GetAllIngredientsForReportFilter
        // Action này để lấy tất cả NVL cho dropdown trong modal báo cáo, không phân trang
        [HttpGet]
        public async Task<IActionResult> GetAllIngredientsForReportFilter()
        {
            var ingredients = await _context.Ingredients
                                    .Where(i => !i.DeleteTemp && i.IsActive) // Chỉ lấy NVL đang hoạt động
                                    .OrderBy(i => i.Name)
                                    .Select(i => new { id = i.Id, text = $"{i.Name} ({i.UnitOfMeasure})" })
                                    .ToListAsync();
            return Ok(ingredients);
        }


        // GET: /Admin/Inventory/GetSuppliersForDropdown
        [HttpGet]
        public async Task<IActionResult> GetSuppliersForDropdown(string searchTerm)
        {
            var query = _context.Suppliers
                               .Where(s => !s.DeleteTemp);

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                query = query.Where(s => s.Name.Contains(searchTerm));
            }
            var suppliers = await query
                                    .OrderBy(s => s.Name)
                                    .Take(20)
                                    .Select(s => new { id = s.Id, text = s.Name })
                                    .ToListAsync();
            return Ok(suppliers);
        }

        // GET: /Admin/Inventory/GetUnitsOfMeasureForDropdown
        [HttpGet]
        public async Task<IActionResult> GetUnitsOfMeasureForDropdown()
        {
            var units = await _context.UnitOfMeasures // Giả sử bạn có bảng UnitOfMeasures
                                      .Where(u => u.IsActive && !u.DeleteTemp)
                                      .OrderBy(u => u.NameUnitOfMeasure)
                                      .Select(u => new { id = u.NameUnitOfMeasure, text = u.NameUnitOfMeasure })
                                      .Distinct()
                                      .ToListAsync();
            return Ok(units);
        }
        #endregion
    }
}