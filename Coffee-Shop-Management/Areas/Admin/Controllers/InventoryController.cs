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

        #region Dashboard API

        // API cho các thẻ KPI
        [HttpGet]
        public async Task<IActionResult> GetDashboardKpis()
        {
            var ingredients = await _context.Ingredients
                                            .Where(i => !i.DeleteTemp && i.IsActive)
                                            .ToListAsync();

            var kpi = new DashboardKpiVM
            {
                TotalInventoryValue = ingredients.Sum(i => i.CurrentStockLevel * i.LastPurchasePrice),
                LowStockItemsCount = ingredients.Count(i => i.MinimumStockLevel.HasValue && i.CurrentStockLevel < i.MinimumStockLevel.Value)
            };

            return Ok(kpi);
        }

        // API cho biểu đồ tròn Phân bổ giá trị kho
        [HttpGet]
        public async Task<IActionResult> GetInventoryValueDistribution()
        {
            var data = await _context.Ingredients
                .Where(i => !i.DeleteTemp && i.IsActive && i.CurrentStockLevel > 0)
                .Select(i => new ChartItem
                {
                    Label = i.Name,
                    Value = i.CurrentStockLevel * i.LastPurchasePrice
                })
                .OrderByDescending(x => x.Value)
                .ToListAsync();

            // Gom các mục nhỏ vào thành "Khác"
            const int topN = 6;
            if (data.Count > topN)
            {
                var topItems = data.Take(topN).ToList();
                var otherValue = data.Skip(topN).Sum(x => x.Value);
                if (otherValue > 0)
                {
                    topItems.Add(new ChartItem { Label = "Khác", Value = otherValue });
                }
                return Ok(topItems);
            }

            return Ok(data);
        }

        // API cho biểu đồ Top 10 NVL tiêu thụ
        [HttpGet]
        public async Task<IActionResult> GetTopConsumedIngredients()
        {
            var thirtyDaysAgo = DateTime.Now.AddDays(-30);
            var data = await _context.InventoryTransactions
                .Where(t => (t.TransactionType == InventoryTransactionType.SaleConsumption || t.TransactionType == InventoryTransactionType.AdjustmentOut) && t.TransactionDate >= thirtyDaysAgo)
                .Include(t => t.Ingredient)
                .GroupBy(t => t.Ingredient.Name)
                .Select(g => new ChartItem
                {
                    Label = g.Key,
                    Value = g.Sum(t => Math.Abs(t.QuantityChanged)) // Lấy số dương
                })
                .OrderByDescending(x => x.Value)
                .Take(10)
                .ToListAsync();

            return Ok(data);
        }

        // API cho biểu đồ xu hướng hàng hủy
        [HttpGet]
        public async Task<IActionResult> GetWasteTrend()
        {
            var sixMonthsAgo = DateTime.Now.AddMonths(-6);
            var monthlyWaste = await _context.InventoryTransactions
                .Where(t => t.TransactionType == InventoryTransactionType.AdjustmentOut && t.TransactionDate >= sixMonthsAgo)
                .GroupBy(t => new { t.TransactionDate.Year, t.TransactionDate.Month })
                .Select(g => new
                {
                    Year = g.Key.Year,
                    Month = g.Key.Month,
                    TotalValue = g.Sum(t => Math.Abs(t.TotalPrice ?? 0))
                })
                .OrderBy(x => x.Year).ThenBy(x => x.Month)
                .ToListAsync();

            // Tạo danh sách 6 tháng gần nhất để đảm bảo không thiếu tháng
            var result = new List<MonthlyData>();
            for (int i = 5; i >= 0; i--)
            {
                var date = DateTime.Now.AddMonths(-i);
                var wasteData = monthlyWaste.FirstOrDefault(w => w.Year == date.Year && w.Month == date.Month);
                result.Add(new MonthlyData
                {
                    Month = date.ToString("MM/yyyy"),
                    Value = wasteData?.TotalValue ?? 0
                });
            }

            return Ok(result);
        }

        // API mới: Lấy danh sách các NVL dưới định mức
        [HttpGet]
        public async Task<IActionResult> GetLowStockIngredients()
        {
            var data = await _context.Ingredients
                .Where(i => !i.DeleteTemp && i.IsActive && i.MinimumStockLevel.HasValue && i.CurrentStockLevel < i.MinimumStockLevel.Value)
                .OrderBy(i => i.CurrentStockLevel / i.MinimumStockLevel) // Ưu tiên các mục cạn nhất
                .Select(i => new LowStockIngredientVM
                {
                    Name = i.Name,
                    CurrentStock = i.CurrentStockLevel,
                    MinimumStock = i.MinimumStockLevel.Value
                })
                .Take(10) // Lấy 10 mục cần nhập gấp nhất
                .ToListAsync();

            return Ok(data);
        }

        // API mới: Lấy Top NVL bị hủy/thất thoát theo giá trị
        [HttpGet]
        public async Task<IActionResult> GetTopWastedIngredients()
        {
            var thirtyDaysAgo = DateTime.Now.AddDays(-30);
            var data = await _context.InventoryTransactions
                .Where(t => t.TransactionType == InventoryTransactionType.AdjustmentOut && t.TransactionDate >= thirtyDaysAgo)
                .Include(t => t.Ingredient)
                .GroupBy(t => t.Ingredient.Name)
                .Select(g => new WastedItemVM
                {
                    Name = g.Key,
                    QuantityWasted = g.Sum(t => Math.Abs(t.QuantityChanged)),
                    ValueWasted = g.Sum(t => Math.Abs(t.TotalPrice ?? 0))
                })
                .OrderByDescending(x => x.ValueWasted)
                .Take(7) // Lấy top 7
                .ToListAsync();

            return Ok(data);
        }

        // Các ViewModel cho Dashboard API
        public class DashboardKpiVM
        {
            public decimal TotalInventoryValue { get; set; }
            public int LowStockItemsCount { get; set; }
        }

        public class ChartItem
        {
            public string Label { get; set; } = string.Empty;
            public decimal Value { get; set; }
        }

        public class MonthlyData
        {
            public string Month { get; set; } = string.Empty;
            public decimal Value { get; set; }
        }

        public class LowStockIngredientVM
        {
            public string Name { get; set; } = string.Empty;
            public decimal CurrentStock { get; set; }
            public decimal MinimumStock { get; set; }
        }

        public class WastedItemVM
        {
            public string Name { get; set; } = string.Empty;
            public decimal QuantityWasted { get; set; }
            public decimal ValueWasted { get; set; }
        }
        #endregion

        #region Xuất Báo cáo Tồn Kho Chi Tiết Excel

        [HttpPost]
        public async Task<IActionResult> ExportInventoryDetailReport(DateTime fromDate, DateTime toDate, List<int> ingredientIds = null)
        {
            // ... Phần logic lấy dữ liệu giữ nguyên như cũ, không có thay đổi ...
            _logger.LogInformation("Bắt đầu xuất báo cáo tồn kho tổng hợp và chi tiết từ {FromDate} đến {ToDate}", fromDate, toDate);

            if (toDate < fromDate)
            {
                return BadRequest("Ngày kết thúc không thể sớm hơn ngày bắt đầu.");
            }
            var reportTrueEndDate = toDate.Date.AddDays(1).AddTicks(-1);

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
            var detailReportItems = new List<InventoryDetailReportItemVM>();
            int sttSummary = 1;

            foreach (var ingredient in ingredientsToQuery)
            {
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
                        TransactionType = trans.TransactionType,
                        UserName = trans.User?.Name ?? (trans.UserId == null ? "Hệ thống" : "N/A"),
                        SupplierName = trans.Supplier?.Name ?? "",
                        Notes = trans.Notes
                    };

                    if (trans.QuantityChanged > 0)
                    {
                        transVM.QuantityIn = trans.QuantityChanged;
                        transVM.UnitPriceIn = trans.UnitPrice;
                        transVM.ValueIn = trans.QuantityChanged * (trans.UnitPrice ?? 0);
                    }
                    else
                    {
                        transVM.QuantityOut = -trans.QuantityChanged;
                        transVM.UnitPriceOut = trans.UnitPrice ?? ingredient.LastPurchasePrice;
                        transVM.ValueOut = transVM.QuantityOut * transVM.UnitPriceOut;
                    }

                    currentStockForDetail += trans.QuantityChanged;
                    transVM.StockAfterTransaction = currentStockForDetail;
                    detailItem.Transactions.Add(transVM);
                }

                detailItem.ClosingStockQuantity = currentStockForDetail;
                detailItem.ClosingStockUnitPrice = ingredient.LastPurchasePrice;
                detailReportItems.Add(detailItem);

                var closingStockValue = detailItem.OpeningStockValue + detailItem.TotalValueIn - detailItem.TotalValueOut;

                summaryReportItems.Add(new InventorySummaryReportItemVM
                {
                    STT = sttSummary++,
                    IngredientCode = detailItem.IngredientCode,
                    IngredientName = detailItem.IngredientName,
                    UnitOfMeasure = detailItem.UnitOfMeasure,
                    OpeningStockQuantity = detailItem.OpeningStockQuantity,
                    OpeningStockValue = detailItem.OpeningStockValue,
                    PeriodQuantityIn = detailItem.TotalQuantityIn,
                    PeriodValueIn = detailItem.TotalValueIn,
                    PeriodQuantityOut = detailItem.TotalQuantityOut,
                    PeriodValueOut = detailItem.TotalValueOut,
                    ClosingStockQuantity = detailItem.ClosingStockQuantity,
                    ClosingStockValue = closingStockValue,
                    MinimumStockLevel = ingredient.MinimumStockLevel
                });
            }

            using (var workbook = new XLWorkbook())
            {
                var summarySheet = workbook.Worksheets.Add("TongHopNXT");
                RenderSummarySheet(summarySheet, summaryReportItems, fromDate, toDate.Date);

                foreach (var detailItemData in detailReportItems)
                {
                    string rawSheetName = $"{detailItemData.IngredientCode}";
                    string validSheetName = new string(rawSheetName.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray()).Trim();
                    validSheetName = validSheetName.Length > 31 ? validSheetName.Substring(0, 31) : validSheetName;
                    if (workbook.Worksheets.Any(ws => ws.Name.Equals(validSheetName, StringComparison.OrdinalIgnoreCase)))
                    {
                        validSheetName = validSheetName.Substring(0, Math.Min(validSheetName.Length, 28)) + Guid.NewGuid().ToString().Substring(0, 3);
                    }

                    var detailSheet = workbook.Worksheets.Add(validSheetName);
                    RenderDetailSheetForItem(detailSheet, detailItemData);
                }

                summarySheet.SetTabActive();

                using (var stream = new MemoryStream())
                {
                    workbook.SaveAs(stream);
                    var content = stream.ToArray();
                    string fileName = $"BaoCao_TonKho_{DateTime.Now:yyyyMMddHHmmss}.xlsx";
                    return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
                }
            }
        }

        private string GetNumberFormat(decimal value)
        {
            return value == Math.Truncate(value) ? "#,##0" : "#,##0.###";
        }

        private void FormatCellAsNumber(IXLCell cell, decimal value)
        {
            cell.SetValue(value);
            if (value % 1 == 0)
            {
                cell.Style.NumberFormat.Format = "#,##0";
            }
            else
            {
                cell.Style.NumberFormat.Format = "#,##0.###";
            }
        }

        private void RenderSummarySheet(IXLWorksheet sheet, List<InventorySummaryReportItemVM> items, DateTime fromDate, DateTime toDate)
        {
            const string formatVND = "#,##0";

            int currentRow = 1;
            sheet.Cell(currentRow, 1).Value = "LAVY COFFEE & TEA";
            var companyCell = sheet.Range(currentRow, 1, currentRow, 5).Merge();
            companyCell.Style.Font.Bold = true;
            companyCell.Style.Font.FontName = "Times New Roman";

            var dateCell = sheet.Cell(currentRow, 11);
            dateCell.Value = $"Xuất: {DateTime.Now:dd/MM/yyyy HH:mm}";
            var dateCellRange = sheet.Range(currentRow, 11, currentRow, 12).Merge();
            dateCellRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
            dateCellRange.Style.Font.Italic = true;

            currentRow = 3;
            var titleCell = sheet.Cell(currentRow, 1);
            titleCell.Value = "BÁO CÁO TỔNG HỢP NHẬP XUẤT TỒN";
            sheet.Range(currentRow, 1, currentRow, 12).Merge().Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            titleCell.Style.Font.Bold = true;
            titleCell.Style.Font.FontSize = 16;
            titleCell.Style.Font.FontName = "Times New Roman";

            currentRow++;
            var dateRangeCell = sheet.Cell(currentRow, 1);
            dateRangeCell.Value = $"Từ ngày {fromDate:dd/MM/yyyy} đến ngày {toDate:dd/MM/yyyy}";
            sheet.Range(currentRow, 1, currentRow, 12).Merge().Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            dateRangeCell.Style.Font.Italic = true;

            currentRow = 6;
            sheet.Range(currentRow, 1, currentRow, 4).Merge().Value = "Thông tin hàng hóa";
            sheet.Range(currentRow, 5, currentRow, 6).Merge().Value = "Tồn đầu kỳ";
            sheet.Range(currentRow, 7, currentRow, 8).Merge().Value = "Nhập trong kỳ";
            sheet.Range(currentRow, 9, currentRow, 10).Merge().Value = "Xuất trong kỳ";
            sheet.Range(currentRow, 11, currentRow, 12).Merge().Value = "Tồn cuối kỳ";

            var mainHeaderRange = sheet.Range(currentRow, 1, currentRow, 12);
            mainHeaderRange.Style.Font.Bold = true;
            mainHeaderRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            mainHeaderRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            mainHeaderRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#DDEBF7");
            mainHeaderRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;

            currentRow++;
            var headers2 = new[] { "STT", "Mã hàng", "Tên hàng", "ĐVT", "Số Lượng", "Thành Tiền", "Số Lượng", "Thành Tiền", "Số Lượng", "Thành Tiền", "Số Lượng", "Thành Tiền" };
            for (int i = 0; i < headers2.Length; i++) { sheet.Cell(currentRow, i + 1).Value = headers2[i]; }

            var detailHeaderRange = sheet.Range(currentRow, 1, currentRow, headers2.Length);
            detailHeaderRange.Style.Font.Bold = true;
            detailHeaderRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            detailHeaderRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            detailHeaderRange.Style.Alignment.WrapText = true;
            detailHeaderRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#DDEBF7");
            detailHeaderRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            detailHeaderRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            sheet.Row(currentRow).Height = 30;

            currentRow++;
            int firstDataRow = currentRow;
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                int col = 1;
                sheet.Cell(currentRow, col++).SetValue(item.STT);
                sheet.Cell(currentRow, col++).SetValue(item.IngredientCode);
                sheet.Cell(currentRow, col++).SetValue(item.IngredientName);
                sheet.Cell(currentRow, col++).SetValue(item.UnitOfMeasure);

                FormatCellAsNumber(sheet.Cell(currentRow, col++), item.OpeningStockQuantity);
                sheet.Cell(currentRow, col++).SetValue(item.OpeningStockValue).Style.NumberFormat.Format = formatVND;
                FormatCellAsNumber(sheet.Cell(currentRow, col++), item.PeriodQuantityIn);
                sheet.Cell(currentRow, col++).SetValue(item.PeriodValueIn).Style.NumberFormat.Format = formatVND;
                FormatCellAsNumber(sheet.Cell(currentRow, col++), item.PeriodQuantityOut);
                sheet.Cell(currentRow, col++).SetValue(item.PeriodValueOut).Style.NumberFormat.Format = formatVND;
                FormatCellAsNumber(sheet.Cell(currentRow, col++), item.ClosingStockQuantity);
                sheet.Cell(currentRow, col++).SetValue(item.ClosingStockValue).Style.NumberFormat.Format = formatVND;

                if (item.MinimumStockLevel.HasValue && item.ClosingStockQuantity < item.MinimumStockLevel.Value)
                {
                    sheet.Row(currentRow).Style.Font.FontColor = XLColor.Red;
                }

                if (i % 2 != 0)
                {
                    sheet.Range(currentRow, 1, currentRow, 12).Style.Fill.BackgroundColor = XLColor.FromHtml("#F2F2F2");
                }
                currentRow++;
            }

            var totalLabelRange = sheet.Range(currentRow, 1, currentRow, 4).Merge();
            totalLabelRange.Value = "Tổng cộng";
            totalLabelRange.Style.Font.Bold = true;
            totalLabelRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#FFF2CC");
            totalLabelRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            var cellsToSum = new[] { 6, 8, 10, 12 };
            foreach (var colIndex in cellsToSum)
            {
                var cell = sheet.Cell(currentRow, colIndex);
                cell.FormulaA1 = $"=SUM({sheet.Cell(firstDataRow, colIndex).Address.ToStringFixed()}:{sheet.Cell(currentRow - 1, colIndex).Address.ToStringFixed()})";
                cell.Style.NumberFormat.Format = formatVND;
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#FFF2CC");
            }

            var tableRange = sheet.Range(firstDataRow - 1, 1, currentRow, 12);
            tableRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            tableRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            tableRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            sheet.Range(firstDataRow, 1, currentRow, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            sheet.Range(firstDataRow, 5, currentRow, 12).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

            sheet.Columns().AdjustToContents();
            sheet.Column(3).Width = 35;
        }

        private void RenderDetailSheetForItem(IXLWorksheet sheet, InventoryDetailReportItemVM itemData)
        {
            const string formatVND = "#,##0";
            var colorPurchase = XLColor.FromHtml("#E7F3E7");
            var colorSale = XLColor.FromHtml("#FFF0F0");
            var colorAdjustmentIn = XLColor.FromHtml("#EBF5FF");
            var colorAdjustmentOut = XLColor.FromHtml("#FFF9E6");

            int currentRow = 1;
            var titleCell = sheet.Cell(currentRow, 1);
            titleCell.Value = "THẺ KHO (CHI TIẾT NHẬP XUẤT TỒN)";
            sheet.Range(currentRow, 1, currentRow, 13).Merge().Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            titleCell.Style.Font.Bold = true;
            titleCell.Style.Font.FontSize = 14;
            titleCell.Style.Font.FontName = "Times New Roman";

            currentRow += 2;
            sheet.Cell(currentRow, 1).Value = $"Nguyên vật liệu: {itemData.IngredientName} ({itemData.IngredientCode})";
            sheet.Range(currentRow, 1, currentRow, 5).Merge().Style.Font.Bold = true;
            sheet.Cell(currentRow, 7).Value = $"ĐVT: {itemData.UnitOfMeasure}";
            sheet.Cell(currentRow, 10).Value = $"Tồn tối thiểu: {(itemData.MinimumStockLevel.HasValue ? itemData.MinimumStockLevel.Value.ToString(GetNumberFormat(itemData.MinimumStockLevel.Value)) : "-")}";
            currentRow++;
            sheet.Cell(currentRow, 1).Value = $"Kỳ báo cáo: {itemData.PeriodStartDate:dd/MM/yyyy} - {itemData.PeriodEndDate:dd/MM/yyyy}";
            sheet.Range(currentRow, 1, currentRow, 13).Merge().Style.Font.Italic = true;

            currentRow += 2;
            var headers = new[] { "Ngày GD", "Số Phiếu", "Loại GD/Diễn giải", "Người TH", "NCC", "SL Nhập", "ĐG Nhập", "TT Nhập", "SL Xuất", "ĐG Xuất", "TT Xuất", "SL Tồn", "Ghi Chú" };
            for (int i = 0; i < headers.Length; i++) { sheet.Cell(currentRow, i + 1).Value = headers[i]; }

            var headerRange = sheet.Range(currentRow, 1, currentRow, headers.Length);
            headerRange.Style.Font.Bold = true;
            headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            headerRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            headerRange.Style.Alignment.WrapText = true;
            headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#DDEBF7");
            headerRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            headerRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;

            currentRow++;
            sheet.Cell(currentRow, 1).SetValue(itemData.PeriodStartDate.AddDays(-1)).Style.DateFormat.Format = "dd/MM/yyyy";
            sheet.Range(currentRow, 2, currentRow, 11).Merge().SetValue("Tồn đầu kỳ").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            FormatCellAsNumber(sheet.Cell(currentRow, 12), itemData.OpeningStockQuantity);
            sheet.Row(currentRow).Style.Font.Italic = true;
            sheet.Row(currentRow).Style.Font.Bold = true;

            currentRow++;
            int firstDataRow = currentRow;

            Action<IXLCell, decimal?, bool> fillNullable = (cell, val, isQuantity) => {
                if (val.HasValue)
                {
                    if (isQuantity)
                    {
                        FormatCellAsNumber(cell, val.Value);
                    }
                    else
                    {
                        cell.SetValue(val.Value).Style.NumberFormat.Format = formatVND;
                    }
                }
                else
                {
                    cell.SetValue("-");
                }
            };

            for (int i = 0; i < itemData.Transactions.Count; i++)
            {
                var trans = itemData.Transactions[i];
                sheet.Cell(currentRow, 1).SetValue(trans.TransactionDate);
                sheet.Cell(currentRow, 2).SetValue(trans.TransactionId);
                sheet.Cell(currentRow, 3).SetValue(trans.TransactionTypeDisplay);
                sheet.Cell(currentRow, 4).SetValue(trans.UserName);
                sheet.Cell(currentRow, 5).SetValue(trans.SupplierName);

                var stockAfterCell = sheet.Cell(currentRow, 12);
                FormatCellAsNumber(stockAfterCell, trans.StockAfterTransaction);

                sheet.Cell(currentRow, 13).SetValue(trans.Notes);

                if (trans.StockAfterTransaction < 0)
                {
                    stockAfterCell.Style.Font.FontColor = XLColor.Red;
                }

                fillNullable(sheet.Cell(currentRow, 6), trans.QuantityIn, true);
                fillNullable(sheet.Cell(currentRow, 7), trans.UnitPriceIn, false);
                fillNullable(sheet.Cell(currentRow, 8), trans.ValueIn, false);
                fillNullable(sheet.Cell(currentRow, 9), trans.QuantityOut, true);
                fillNullable(sheet.Cell(currentRow, 10), trans.UnitPriceOut, false);
                fillNullable(sheet.Cell(currentRow, 11), trans.ValueOut, false);

                var dataRange = sheet.Range(currentRow, 1, currentRow, 13);
                switch (trans.TransactionType)
                {
                    case AppDbContext.InventoryTransactionType.Purchase:
                    case AppDbContext.InventoryTransactionType.InitialStock:
                        dataRange.Style.Fill.BackgroundColor = colorPurchase;
                        break;
                    case AppDbContext.InventoryTransactionType.AdjustmentIn:
                        dataRange.Style.Fill.BackgroundColor = colorAdjustmentIn;
                        break;
                    case AppDbContext.InventoryTransactionType.SaleConsumption:
                        dataRange.Style.Fill.BackgroundColor = colorSale;
                        break;
                    case AppDbContext.InventoryTransactionType.AdjustmentOut:
                        dataRange.Style.Fill.BackgroundColor = colorAdjustmentOut;
                        break;
                }

                currentRow++;
            }

            var summaryLabelRange = sheet.Range(currentRow, 1, currentRow, 5).Merge();
            summaryLabelRange.Value = "Tổng cộng phát sinh";
            summaryLabelRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#FFF2CC");

            FormatCellAsNumber(sheet.Cell(currentRow, 6), itemData.TotalQuantityIn);
            sheet.Cell(currentRow, 8).SetValue(itemData.TotalValueIn).Style.NumberFormat.Format = formatVND;
            FormatCellAsNumber(sheet.Cell(currentRow, 9), itemData.TotalQuantityOut);
            sheet.Cell(currentRow, 11).SetValue(itemData.TotalValueOut).Style.NumberFormat.Format = formatVND;

            var summaryRow = sheet.Row(currentRow);
            summaryRow.Style.Font.Bold = true;
            summaryRow.Cells(6, 11).Style.Fill.BackgroundColor = XLColor.FromHtml("#FFF2CC");

            currentRow++;
            sheet.Range(currentRow, 1, currentRow, 11).Merge().Value = "Tồn cuối kỳ";
            var closingStockCell = sheet.Cell(currentRow, 12);
            FormatCellAsNumber(closingStockCell, itemData.ClosingStockQuantity);
            sheet.Row(currentRow).Style.Font.Italic = true;
            sheet.Row(currentRow).Style.Font.Bold = true;

            var tableRange = sheet.Range(firstDataRow - 1, 1, currentRow, 13);
            tableRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            tableRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            tableRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

            sheet.Range(firstDataRow, 1, currentRow - 2, 1).Style.DateFormat.Format = "dd/MM/yyyy HH:mm";
            sheet.Range(firstDataRow, 2, currentRow - 2, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            sheet.Range(firstDataRow - 1, 6, currentRow, 12).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

            if (itemData.QuantityToOrder.HasValue && itemData.QuantityToOrder.Value > 0)
            {
                currentRow += 2;
                sheet.Cell(currentRow, 3).Value = "Số lượng cần nhập thêm (dự kiến):";
                var quantityToOrderCell = sheet.Cell(currentRow, 6);
                FormatCellAsNumber(quantityToOrderCell, itemData.QuantityToOrder.Value);
                var rangeToColor = sheet.Range(currentRow, 3, currentRow, 6);
                rangeToColor.Style.Font.Bold = true;
                rangeToColor.Style.Font.FontColor = XLColor.Red;
            }

            // *** [SỬA LỖI] BẬT TÍNH NĂNG TỰ ĐỘNG XUỐNG DÒNG CHO CỘT GHI CHÚ ***
            sheet.Column(3).Style.Alignment.WrapText = true;
            sheet.Column(4).Style.Alignment.WrapText = true;
            sheet.Column(5).Style.Alignment.WrapText = true;
            sheet.Column(13).Style.Alignment.WrapText = true;
            // Căn lề trên cho đẹp khi có nhiều dòng
            sheet.Column(13).Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;

            sheet.Columns().AdjustToContents();
            sheet.Column(3).Width = 15;
            sheet.Column(4).Width = 15;
            sheet.Column(5).Width = 20;
            sheet.Column(13).Width = 25; // Giới hạn lại độ rộng cột ghi chú
        }
        #endregion

        [HttpPost]
        public async Task<IActionResult> PreviewInventoryReport(DateTime fromDate, DateTime toDate, List<int> ingredientIds = null)
        {
            if (toDate < fromDate)
            {
                return BadRequest("Ngày kết thúc không thể sớm hơn ngày bắt đầu.");
            }

            var reportTrueEndDate = toDate.Date.AddDays(1).AddTicks(-1);

            List<Ingredient> ingredientsToQuery;
            if (ingredientIds == null || !ingredientIds.Any())
            {
                ingredientsToQuery = await _context.Ingredients.Where(i => !i.DeleteTemp && i.IsActive).OrderBy(i => i.Name).ToListAsync();
            }
            else
            {
                ingredientsToQuery = await _context.Ingredients.Where(i => ingredientIds.Contains(i.Id) && !i.DeleteTemp).OrderBy(i => i.Name).ToListAsync();
            }

            if (!ingredientsToQuery.Any())
            {
                return PartialView("_InventoryReportPreview", new List<InventorySummaryReportItemVM>());
            }

            var summaryReportItems = new List<InventorySummaryReportItemVM>();
            int sttSummary = 1;

            foreach (var ingredient in ingredientsToQuery)
            {
                // Lấy giao dịch trước kỳ để tính tồn đầu
                var transactionsBeforePeriod = await _context.InventoryTransactions
                    .Where(t => t.IngredientId == ingredient.Id && t.TransactionDate < fromDate)
                    .ToListAsync();

                decimal openingStockQuantity = transactionsBeforePeriod.Sum(t => t.QuantityChanged);

                // Lấy giá vốn cuối cùng trước kỳ
                var lastPurchaseBefore = transactionsBeforePeriod
                    .Where(t => t.QuantityChanged > 0 && t.UnitPrice.HasValue && t.UnitPrice > 0)
                    .OrderByDescending(t => t.TransactionDate).ThenByDescending(t => t.Id)
                    .FirstOrDefault();
                decimal openingStockUnitPrice = lastPurchaseBefore?.UnitPrice ?? ingredient.LastPurchasePrice;
                decimal openingStockValue = openingStockQuantity * openingStockUnitPrice;

                // Lấy giao dịch trong kỳ
                var transactionsInPeriod = await _context.InventoryTransactions
                    .Where(t => t.IngredientId == ingredient.Id && t.TransactionDate >= fromDate && t.TransactionDate <= reportTrueEndDate)
                    .ToListAsync();

                decimal periodQuantityIn = transactionsInPeriod.Where(t => t.QuantityChanged > 0).Sum(t => t.QuantityChanged);
                decimal periodValueIn = transactionsInPeriod.Where(t => t.QuantityChanged > 0).Sum(t => t.TotalPrice ?? (t.QuantityChanged * (t.UnitPrice ?? 0)));

                decimal periodQuantityOut = transactionsInPeriod.Where(t => t.QuantityChanged < 0).Sum(t => -t.QuantityChanged);
                decimal periodValueOut = transactionsInPeriod.Where(t => t.QuantityChanged < 0).Sum(t => Math.Abs(t.TotalPrice ?? (-t.QuantityChanged * (t.UnitPrice ?? ingredient.LastPurchasePrice))));

                decimal closingStockQuantity = openingStockQuantity + periodQuantityIn - periodQuantityOut;
                decimal closingStockValue = openingStockValue + periodValueIn - periodValueOut;

                summaryReportItems.Add(new InventorySummaryReportItemVM
                {
                    STT = sttSummary++,
                    IngredientId = ingredient.Id,
                    IngredientCode = $"NVL{ingredient.Id:D4}",
                    IngredientName = ingredient.Name,
                    UnitOfMeasure = ingredient.UnitOfMeasure,
                    OpeningStockQuantity = openingStockQuantity,
                    OpeningStockValue = openingStockValue,
                    PeriodQuantityIn = periodQuantityIn,
                    PeriodValueIn = periodValueIn,
                    PeriodQuantityOut = periodQuantityOut,
                    PeriodValueOut = periodValueOut,
                    ClosingStockQuantity = closingStockQuantity,
                    ClosingStockValue = closingStockValue
                });
            }

            return PartialView("_InventoryReportPreview", summaryReportItems);
        }

        [HttpPost]
        public async Task<IActionResult> PreviewIngredientDetail(int ingredientId, DateTime fromDate, DateTime toDate)
        {
            var ingredient = await _context.Ingredients.FindAsync(ingredientId);
            if (ingredient == null)
            {
                return NotFound("Không tìm thấy nguyên vật liệu.");
            }

            var reportTrueEndDate = toDate.Date.AddDays(1).AddTicks(-1);

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
                    TransactionType = trans.TransactionType,
                    UserName = trans.User?.Name ?? "Hệ thống",
                    SupplierName = trans.Supplier?.Name ?? "",
                    Notes = trans.Notes
                };

                if (trans.QuantityChanged > 0)
                {
                    transVM.QuantityIn = trans.QuantityChanged;
                    transVM.UnitPriceIn = trans.UnitPrice;
                    transVM.ValueIn = trans.QuantityChanged * (trans.UnitPrice ?? 0);
                }
                else
                {
                    transVM.QuantityOut = -trans.QuantityChanged;
                    transVM.UnitPriceOut = trans.UnitPrice ?? ingredient.LastPurchasePrice;
                    transVM.ValueOut = transVM.QuantityOut * transVM.UnitPriceOut;
                }

                currentStockForDetail += trans.QuantityChanged;
                transVM.StockAfterTransaction = currentStockForDetail;
                detailItem.Transactions.Add(transVM);
            }

            // *** [SỬA LỖI] Gán giá trị tồn kho cuối kỳ đã tính được vào model ***
            detailItem.ClosingStockQuantity = currentStockForDetail;

            return PartialView("_IngredientDetailPreview", detailItem);
        }


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

        #region Giao dịch Kho (Inventory Transactions) - Bổ sung hàm GetTransactionTypeDisplayName nếu chưa có ngoài controller

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
        // ##### PHẦN CODE ĐÃ ĐƯỢC CẬP NHẬT #####
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

            // [START] === CẬP NHẬT LOGIC VALIDATION ===
            // Bắt buộc nhập UnitPrice cho Nhập mua, Tồn đầu kỳ, và cả hai loại Điều chỉnh
            if ((model.TransactionType == InventoryTransactionType.Purchase ||
                 model.TransactionType == InventoryTransactionType.InitialStock ||
                 model.TransactionType == InventoryTransactionType.AdjustmentIn ||
                 model.TransactionType == InventoryTransactionType.AdjustmentOut)
                 && !model.UnitPrice.HasValue)
            {
                ModelState.AddModelError("UnitPrice", "Đơn giá là bắt buộc cho loại giao dịch này.");
            }

            // Giữ nguyên validation cho SupplierId
            if (model.TransactionType == InventoryTransactionType.Purchase && !model.SupplierId.HasValue)
            {
                ModelState.AddModelError("SupplierId", "Nhà cung cấp là bắt buộc cho loại giao dịch mua hàng.");
            }

            if (!ModelState.IsValid)
            {
                return BadRequest(new { success = false, errors = ModelState.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Errors.Select(e => e.ErrorMessage).ToArray()) });
            }
            // [END] === CẬP NHẬT LOGIC VALIDATION ===

            var currentUser = await _userManager.GetUserAsync(User);
            if (currentUser == null)
            {
                return Unauthorized(new { success = false, message = "Yêu cầu đăng nhập." });
            }

            var transaction = new InventoryTransaction
            {
                IngredientId = model.IngredientId,
                TransactionType = model.TransactionType,
                TransactionDate = DateTime.Now,
                QuantityChanged = model.Quantity, // Sẽ điều chỉnh dấu ở dưới
                UnitPrice = model.UnitPrice,
                SupplierId = model.TransactionType == InventoryTransactionType.Purchase ? model.SupplierId : null,
                UserId = currentUser.Id,
                Notes = model.Notes,
                CreatedAt = DateTime.Now
            };

            // [START] === CẬP NHẬT LOGIC XỬ LÝ GIAO DỊCH ===
            switch (model.TransactionType)
            {
                case InventoryTransactionType.InitialStock:
                case InventoryTransactionType.Purchase:
                case InventoryTransactionType.AdjustmentIn:
                    ingredient.CurrentStockLevel += model.Quantity;
                    transaction.QuantityChanged = model.Quantity; // Đảm bảo số lượng là dương

                    // Với Nhập mua hoặc Nhập điều chỉnh có giá > 0, cập nhật LastPurchasePrice
                    // Cả Tồn đầu kỳ cũng nên cập nhật LastPurchasePrice nếu đây là lần đầu tiên
                    if (model.UnitPrice.HasValue && model.UnitPrice.Value > 0)
                    {
                        ingredient.LastPurchasePrice = model.UnitPrice.Value;
                    }
                    if (transaction.UnitPrice.HasValue)
                    {
                        transaction.TotalPrice = transaction.QuantityChanged * transaction.UnitPrice.Value;
                    }
                    break;

                case InventoryTransactionType.SaleConsumption:
                case InventoryTransactionType.AdjustmentOut:
                    if (ingredient.CurrentStockLevel < model.Quantity && model.TransactionType == InventoryTransactionType.SaleConsumption)
                    {
                        return BadRequest(new { success = false, message = $"Không đủ tồn kho cho '{ingredient.Name}'. Tồn kho hiện tại: {ingredient.CurrentStockLevel.ToString("N3", _vnCulture)} {ingredient.UnitOfMeasure}." });
                    }
                    ingredient.CurrentStockLevel -= model.Quantity;
                    transaction.QuantityChanged = -model.Quantity; // Đảm bảo số lượng là âm

                    // Đối với AdjustmentOut, UnitPrice đã được validate là bắt buộc.
                    // Đối với SaleConsumption, UnitPrice có thể không có và sẽ lấy giá vốn cuối cùng.
                    if (!transaction.UnitPrice.HasValue)
                    {
                        transaction.UnitPrice = ingredient.LastPurchasePrice;
                    }
                    // Luôn tính TotalPrice dựa trên giá trị thực tế và đảm bảo nó là số âm.
                    if (transaction.UnitPrice.HasValue)
                    {
                        transaction.TotalPrice = transaction.QuantityChanged * transaction.UnitPrice.Value;
                    }
                    break;
            }
            // [END] === CẬP NHẬT LOGIC XỬ LÝ GIAO DỊCH ===

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