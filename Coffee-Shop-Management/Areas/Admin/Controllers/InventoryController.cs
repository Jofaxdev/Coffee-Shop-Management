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


        #region Dashboard API (Phiên bản cuối cùng)

        // --- CÁC VIEWMODEL CHO DASHBOARD ---

        /// <summary>
        /// ViewModel chính chứa dữ liệu tổng quan cho dashboard.
        /// </summary>
        public class InventoryDashboardVM
        {
            // KPIs
            public decimal TotalInventoryValue { get; set; }
            public int LowStockItemsCount { get; set; }
            public decimal Last30DaysRevenue { get; set; }
            public decimal Last30DaysProfit { get; set; }
            public decimal WasteValueThisYear { get; set; }

            // Dữ liệu cho các biểu đồ phụ
            public List<ChartItem> InventoryValueDistribution { get; set; }
            public List<LowStockIngredientVM> LowStockIngredients { get; set; }
        }

        /// <summary>
        /// ViewModel chung cho các biểu đồ (cột, tròn).
        /// </summary>
        public class ChartItem
        {
            public string Label { get; set; } = string.Empty;
            public decimal Value { get; set; }
        }

        /// <summary>
        /// ViewModel cho danh sách NVL sắp hết hàng.
        /// (Đã được cập nhật để có thêm Đơn vị tính)
        /// </summary>
        public class LowStockIngredientVM
        {
            public string Name { get; set; } = string.Empty;
            public string UnitOfMeasure { get; set; } // Đã thêm
            public decimal CurrentStock { get; set; }
            public decimal MinimumStock { get; set; }
        }

        /// <summary>
        /// ViewModel cho bảng chi tiết NVL thất thoát.
        /// </summary>
        public class WastedIngredientDetailVM
        {
            public string Name { get; set; }
            public string UnitOfMeasure { get; set; }
            public decimal Quantity { get; set; }
            public decimal Value { get; set; }
        }


        // --- CÁC API ACTION ---

        /// <summary>
        /// Lấy dữ liệu tổng hợp cho các KPI và biểu đồ chính trên dashboard.
        /// Doanh thu, lợi nhuận và thất thoát được tính từ đầu năm đến ngày hiện tại.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetDashboardData()
        {
            try
            {
                var today = DateTime.Now;
                var startOfYear = new DateTime(today.Year, 1, 1);

                // --- TÍNH TOÁN CÁC KPI ---
                var totalInventoryValue = await _context.InventoryBatches
                    .Where(b => b.IsActive && b.Quantity > 0).SumAsync(b => b.Quantity * b.PurchasePrice);

                var saleTransactionsInPeriod = await _context.InventoryTransactions
                    .Where(t => t.TransactionType == InventoryTransactionType.SaleConsumption && t.TransactionDate >= startOfYear && t.TransactionDate <= today)
                    .Include(t => t.Batch).ToListAsync();

                var revenueThisYear = saleTransactionsInPeriod.Sum(t => t.TotalPrice ?? 0);
                var cogsThisYear = saleTransactionsInPeriod.Sum(t => Math.Abs(t.QuantityChanged) * (t.Batch?.PurchasePrice ?? 0));
                var profitThisYear = revenueThisYear - cogsThisYear;

                var wasteValueThisYear = await _context.InventoryTransactions
                    .Where(t => t.TransactionType == InventoryTransactionType.AdjustmentOut && t.TransactionDate >= startOfYear && t.TransactionDate <= today)
                    .SumAsync(t => Math.Abs(t.TotalPrice ?? 0));

                // --- TÍNH TOÁN DỮ LIỆU BIỂU ĐỒ & BẢNG ---
                var activeIngredients = await _context.Ingredients
                    .Where(i => !i.DeleteTemp && i.IsActive).ToListAsync();
                var lowStockItemsCount = activeIngredients.Count(i => i.MinimumStockLevel.HasValue && i.CurrentStockLevel < i.MinimumStockLevel.Value);

                var distributionDataRaw = await _context.InventoryBatches
                    .Where(b => b.IsActive && b.Quantity > 0 && b.Ingredient != null && b.Ingredient.Name != null)
                    .GroupBy(b => b.Ingredient.Name)
                    .Select(g => new ChartItem { Label = g.Key, Value = g.Sum(b => b.Quantity * b.PurchasePrice) })
                    .OrderByDescending(x => x.Value).Take(7).ToListAsync();

                // Cập nhật câu truy vấn để lấy thêm UnitOfMeasure
                var lowStockIngredientsData = activeIngredients
                    .Where(i => i.MinimumStockLevel.HasValue && i.CurrentStockLevel < i.MinimumStockLevel.Value)
                    .OrderBy(i => i.CurrentStockLevel / i.MinimumStockLevel)
                    .Select(i => new LowStockIngredientVM
                    {
                        Name = i.Name,
                        UnitOfMeasure = i.UnitOfMeasure, // Thêm đơn vị tính
                        CurrentStock = i.CurrentStockLevel,
                        MinimumStock = i.MinimumStockLevel.Value
                    })
                    .Take(10).ToList();

                // --- TẠO VIEWMODEL TRẢ VỀ ---
                var viewModel = new InventoryDashboardVM
                {
                    TotalInventoryValue = totalInventoryValue,
                    LowStockItemsCount = lowStockItemsCount,
                    Last30DaysRevenue = revenueThisYear,
                    Last30DaysProfit = profitThisYear,
                    WasteValueThisYear = wasteValueThisYear,
                    InventoryValueDistribution = distributionDataRaw,
                    LowStockIngredients = lowStockIngredientsData,
                };

                return Ok(viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi nghiêm trọng trong GetDashboardData");
                return StatusCode(500, $"Lỗi từ máy chủ: {ex.Message}\n{ex.StackTrace}");
            }
        }

        /// <summary>
        /// API lấy lợi nhuận theo từng tháng của một năm cụ thể.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetMonthlyProfitByYear(int year)
        {
            try
            {
                var startDate = new DateTime(year, 1, 1);
                var endDate = startDate.AddYears(1).AddTicks(-1);

                var saleTransactions = await _context.InventoryTransactions
                    .Where(t => t.TransactionType == InventoryTransactionType.SaleConsumption
                                && t.TransactionDate >= startDate && t.TransactionDate <= endDate)
                    .Include(t => t.Batch)
                    .ToListAsync();

                var monthlyProfits = saleTransactions
                    .GroupBy(t => t.TransactionDate.Month)
                    .Select(g => new
                    {
                        Month = g.Key,
                        Profit = g.Sum(t => t.TotalPrice ?? 0) - g.Sum(t => Math.Abs(t.QuantityChanged) * (t.Batch?.PurchasePrice ?? 0))
                    })
                    .ToDictionary(x => x.Month, x => x.Profit);

                var result = new List<ChartItem>();
                for (int i = 1; i <= 12; i++)
                {
                    result.Add(new ChartItem
                    {
                        Label = $"Thg {i}",
                        Value = monthlyProfits.ContainsKey(i) ? monthlyProfits[i] : 0
                    });
                }

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi trong GetMonthlyProfitByYear cho năm {Year}", year);
                return StatusCode(500, $"Lỗi từ máy chủ: {ex.Message}");
            }
        }

        /// <summary>
        /// API lấy danh sách chi tiết các NVL thất thoát theo năm.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetWasteDetailsByYear(int year)
        {
            try
            {
                var startDate = new DateTime(year, 1, 1);
                var endDate = startDate.AddYears(1).AddTicks(-1);

                var wasteDetails = await _context.InventoryTransactions
                    .Where(t => t.TransactionType == InventoryTransactionType.AdjustmentOut
                                && t.TransactionDate >= startDate && t.TransactionDate <= endDate)
                    .Include(t => t.Ingredient)
                    .Where(t => t.Ingredient != null && t.Ingredient.Name != null)
                    .GroupBy(t => new { t.Ingredient.Name, t.Ingredient.UnitOfMeasure })
                    .Select(g => new WastedIngredientDetailVM
                    {
                        Name = g.Key.Name,
                        UnitOfMeasure = g.Key.UnitOfMeasure,
                        Quantity = g.Sum(t => Math.Abs(t.QuantityChanged)),
                        Value = g.Sum(t => Math.Abs(t.TotalPrice ?? 0))
                    })
                    .OrderByDescending(x => x.Value)
                    .ToListAsync();

                return Ok(wasteDetails);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi trong GetWasteDetailsByYear cho năm {Year}", year);
                return StatusCode(500, $"Lỗi từ máy chủ: {ex.Message}");
            }
        }

        /// <summary>
        /// API lấy tổng lợi nhuận theo từng năm có giao dịch.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetAnnualProfitSummary()
        {
            try
            {
                var saleTransactions = await _context.InventoryTransactions
                    .Where(t => t.TransactionType == InventoryTransactionType.SaleConsumption && t.Batch != null)
                    .Select(t => new {
                        Year = t.TransactionDate.Year,
                        Revenue = t.TotalPrice ?? 0,
                        Cost = Math.Abs(t.QuantityChanged) * (t.Batch.PurchasePrice)
                    })
                    .ToListAsync();

                var annualProfits = saleTransactions
                    .GroupBy(t => t.Year)
                    .Select(g => new ChartItem
                    {
                        Label = g.Key.ToString(),
                        Value = g.Sum(t => t.Revenue) - g.Sum(t => t.Cost)
                    })
                    .OrderBy(x => x.Label)
                    .ToList();

                return Ok(annualProfits);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi trong GetAnnualProfitSummary");
                return StatusCode(500, $"Lỗi từ máy chủ: {ex.Message}");
            }
        }

        #endregion


        #region Báo cáo & Xuất file

        [HttpPost]
        public async Task<IActionResult> ExportInventoryDetailReport(DateTime fromDate, DateTime toDate, List<int> ingredientIds = null)
        {
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
                var transactionsBeforePeriod = await _context.InventoryTransactions
                    .Where(t => t.IngredientId == ingredient.Id && t.TransactionDate < fromDate)
                    .ToListAsync();

                decimal openingStockQuantity = transactionsBeforePeriod.Sum(t => t.QuantityChanged);
                var lastPurchaseBefore = transactionsBeforePeriod
                    .Where(t => t.QuantityChanged > 0 && t.UnitPrice.HasValue && t.UnitPrice > 0)
                    .OrderByDescending(t => t.TransactionDate).ThenByDescending(t => t.Id)
                    .FirstOrDefault();
                decimal openingStockUnitPrice = lastPurchaseBefore?.UnitPrice ?? ingredient.LastPurchasePrice;
                decimal openingStockValue = openingStockQuantity * openingStockUnitPrice;

                var transactionsInPeriod = await _context.InventoryTransactions
                    .Include(t => t.User)
                    .Include(t => t.Supplier)
                    .Include(t => t.Batch)
                    .Where(t => t.IngredientId == ingredient.Id && t.TransactionDate >= fromDate && t.TransactionDate <= reportTrueEndDate)
                    .OrderBy(t => t.TransactionDate).ThenBy(t => t.Id)
                    .ToListAsync();

                var detailItem = new InventoryDetailReportItemVM
                {
                    IngredientId = ingredient.Id,
                    IngredientCode = $"NVL{ingredient.Id:D4}",
                    IngredientName = ingredient.Name,
                    UnitOfMeasure = ingredient.UnitOfMeasure,
                    MinimumStockLevel = ingredient.MinimumStockLevel,
                    PeriodStartDate = fromDate,
                    PeriodEndDate = toDate.Date,
                    OpeningStockQuantity = openingStockQuantity,
                    OpeningStockUnitPrice = openingStockUnitPrice
                };

                decimal currentStockForDetail = openingStockQuantity;
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
                        transVM.ValueIn = trans.TotalPrice ?? trans.QuantityChanged * (trans.UnitPrice ?? 0);
                    }
                    else
                    {
                        transVM.QuantityOut = -trans.QuantityChanged;
                        transVM.UnitPriceOut = trans.UnitPrice;
                        transVM.ValueOut = trans.TotalPrice;
                        transVM.CostPriceOut = trans.Batch?.PurchasePrice;
                    }

                    currentStockForDetail += trans.QuantityChanged;
                    transVM.StockAfterTransaction = currentStockForDetail;
                    detailItem.Transactions.Add(transVM);
                }

                detailItem.ClosingStockQuantity = currentStockForDetail;
                detailItem.ClosingStockUnitPrice = ingredient.LastPurchasePrice;
                detailReportItems.Add(detailItem);

                decimal periodQuantityIn = detailItem.TotalQuantityIn;
                decimal periodValueIn = detailItem.TotalValueIn;
                decimal periodQuantityOut = detailItem.TotalQuantityOut;

                decimal periodValueOut_COGS = transactionsInPeriod
                    .Where(t => t.QuantityChanged < 0)
                    .Sum(t => Math.Abs(t.QuantityChanged) * (t.Batch?.PurchasePrice ?? ingredient.LastPurchasePrice));

                decimal periodRevenue = transactionsInPeriod
                    .Where(t => t.TransactionType == InventoryTransactionType.SaleConsumption)
                    .Sum(t => t.TotalPrice ?? 0);

                decimal closingStockValue = openingStockValue + periodValueIn - periodValueOut_COGS;

                summaryReportItems.Add(new InventorySummaryReportItemVM
                {
                    STT = sttSummary++,
                    IngredientCode = detailItem.IngredientCode,
                    IngredientName = detailItem.IngredientName,
                    UnitOfMeasure = detailItem.UnitOfMeasure,
                    OpeningStockQuantity = openingStockQuantity,
                    OpeningStockValue = openingStockValue,
                    PeriodQuantityIn = periodQuantityIn,
                    PeriodValueIn = periodValueIn,
                    PeriodQuantityOut = periodQuantityOut,
                    PeriodValueOut = periodValueOut_COGS,
                    PeriodRevenue = periodRevenue,
                    ClosingStockQuantity = currentStockForDetail,
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

        private void RenderSummarySheet(IXLWorksheet sheet, List<InventorySummaryReportItemVM> items, DateTime fromDate, DateTime toDate)
        {
            const string formatVND = "#,##0";
            const int totalColumns = 13;
            sheet.Style.Font.FontName = "Calibri";

            var exportDateCell = sheet.Range(1, totalColumns - 2, 1, totalColumns).Merge();
            exportDateCell.Value = $"Xuất: {DateTime.Now:dd/MM/yyyy HH:mm}";
            exportDateCell.Style.Font.Italic = true;
            exportDateCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
            exportDateCell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

            sheet.Row(3).Height = 24;
            var titleCell = sheet.Range(3, 1, 3, totalColumns).Merge();
            titleCell.Value = "BÁO CÁO TỔNG HỢP NHẬP XUẤT TỒN";
            titleCell.Style.Font.Bold = true;
            titleCell.Style.Font.FontSize = 18;
            titleCell.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
            titleCell.Style.Alignment.SetVertical(XLAlignmentVerticalValues.Center);

            sheet.Row(4).Height = 18;
            var dateRangeCell = sheet.Range(4, 1, 4, totalColumns).Merge();
            dateRangeCell.Value = $"Từ ngày {fromDate:dd/MM/yyyy} đến ngày {toDate:dd/MM/yyyy}";
            dateRangeCell.Style.Font.Italic = true;
            dateRangeCell.Style.Font.FontSize = 12;
            dateRangeCell.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
            dateRangeCell.Style.Alignment.SetVertical(XLAlignmentVerticalValues.Center);

            int headerRow1 = 6;
            int headerRow2 = 7;
            sheet.Row(headerRow1).Height = 22;
            sheet.Row(headerRow2).Height = 22;

            sheet.Range(headerRow1, 1, headerRow2, 1).Merge().Value = "STT";
            sheet.Range(headerRow1, 2, headerRow2, 2).Merge().Value = "Mã hàng";
            sheet.Range(headerRow1, 3, headerRow2, 3).Merge().Value = "Tên hàng";
            sheet.Range(headerRow1, 4, headerRow2, 4).Merge().Value = "ĐVT";
            sheet.Range(headerRow1, 5, headerRow1, 6).Merge().Value = "Tồn đầu kỳ";
            sheet.Range(headerRow1, 7, headerRow1, 8).Merge().Value = "Nhập trong kỳ";
            sheet.Range(headerRow1, 9, headerRow1, 10).Merge().Value = "Xuất trong kỳ";
            sheet.Range(headerRow1, 11, headerRow1, 12).Merge().Value = "Tồn cuối kỳ";
            sheet.Range(headerRow1, 13, headerRow2, 13).Merge().Value = "Doanh thu";

            var subHeaders = new[] { "Số Lượng", "Thành Tiền", "Số Lượng", "Thành Tiền", "Số Lượng", "Thành Tiền", "Số Lượng", "Thành Tiền" };
            int currentCol = 5;
            foreach (var header in subHeaders)
            {
                sheet.Cell(headerRow2, currentCol++).Value = header;
            }

            var fullHeaderRange = sheet.Range(headerRow1, 1, headerRow2, totalColumns);
            fullHeaderRange.Style.Font.Bold = true;
            fullHeaderRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            fullHeaderRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            fullHeaderRange.Style.Alignment.WrapText = true;
            fullHeaderRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#4472C4");
            fullHeaderRange.Style.Font.FontColor = XLColor.White;

            int firstDataRow = headerRow2 + 1;
            for (int i = 0; i < items.Count; i++)
            {
                int currentRow = firstDataRow + i;
                var item = items[i];
                int col = 1;

                sheet.Row(currentRow).Height = 20;

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
                sheet.Cell(currentRow, col++).SetValue(item.PeriodRevenue).Style.NumberFormat.Format = formatVND;

                if (item.MinimumStockLevel.HasValue && item.ClosingStockQuantity < item.MinimumStockLevel.Value)
                {
                    sheet.Range(currentRow, 1, currentRow, totalColumns).Style.Font.FontColor = XLColor.Red;
                }
                if (i % 2 != 0)
                {
                    sheet.Range(currentRow, 1, currentRow, totalColumns).Style.Fill.BackgroundColor = XLColor.FromHtml("#F2F2F2");
                }
            }

            int lastDataRow = items.Any() ? firstDataRow + items.Count - 1 : firstDataRow;
            int totalRow = lastDataRow >= firstDataRow ? lastDataRow + 1 : firstDataRow;

            sheet.Row(totalRow).Height = 22;
            var totalRowRange = sheet.Range(totalRow, 1, totalRow, totalColumns);
            totalRowRange.Style.Font.Bold = true;
            totalRowRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#FFF2CC");

            var totalLabelRange = sheet.Range(totalRow, 1, totalRow, 4).Merge();
            totalLabelRange.Value = "Tổng cộng";
            totalLabelRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            var cellsToSum = new[] { 6, 8, 10, 12, 13 };
            foreach (var colIndex in cellsToSum)
            {
                var cell = sheet.Cell(totalRow, colIndex);
                if (items.Any())
                {
                    cell.FormulaA1 = $"=SUM({sheet.Cell(firstDataRow, colIndex).Address.ToStringFixed()}:{sheet.Cell(lastDataRow, colIndex).Address.ToStringFixed()})";
                }
                else
                {
                    cell.SetValue(0);
                }
                cell.Style.NumberFormat.Format = formatVND;
            }

            var tableRange = sheet.Range(headerRow1, 1, totalRow, totalColumns);
            tableRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            tableRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;

            tableRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            sheet.Column(3).Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;

            sheet.Column(1).Width = 5;
            sheet.Column(2).Width = 12;
            sheet.Column(3).Width = 40;
            sheet.Column(4).Width = 8;
            sheet.Column(5).Width = 12;
            sheet.Column(6).Width = 15;
            sheet.Column(7).Width = 12;
            sheet.Column(8).Width = 15;
            sheet.Column(9).Width = 12;
            sheet.Column(10).Width = 15;
            sheet.Column(11).Width = 12;
            sheet.Column(12).Width = 15;
            sheet.Column(13).Width = 15;
        }

        private void RenderDetailSheetForItem(IXLWorksheet sheet, InventoryDetailReportItemVM itemData)
        {
            const string formatVND = "#,##0";
            var headers = new[] { "Ngày GD", "Số Phiếu", "Loại GD/Diễn giải", "Người TH", "NCC", "SL Nhập", "ĐG Nhập", "TT Nhập", "SL Xuất", "ĐG Xuất", "TT Xuất", "SL Tồn", "Ghi Chú" };
            sheet.Style.Font.FontName = "Calibri";

            sheet.Row(1).Height = 24;
            var titleCell = sheet.Range(1, 1, 1, headers.Length).Merge();
            titleCell.Value = "CHI TIẾT NHẬP XUẤT TỒN";
            titleCell.Style.Font.Bold = true;
            titleCell.Style.Font.FontSize = 18;
            titleCell.Style.Font.FontColor = XLColor.Black;
            titleCell.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
            titleCell.Style.Alignment.SetVertical(XLAlignmentVerticalValues.Center);

            int infoRow = 3;
            sheet.Row(infoRow).Height = 18;
            sheet.Row(infoRow + 1).Height = 18;
            sheet.Cell(infoRow, 1).Value = "Nguyên vật liệu:";
            sheet.Cell(infoRow, 2).Value = $"{itemData.IngredientName} ({itemData.IngredientCode})";
            sheet.Range(infoRow, 2, infoRow, 5).Merge();
            sheet.Cell(infoRow, 9).Value = "ĐVT:";
            sheet.Cell(infoRow, 10).Value = itemData.UnitOfMeasure;
            sheet.Cell(infoRow + 1, 1).Value = "Kỳ báo cáo:";
            sheet.Cell(infoRow + 1, 2).Value = $"{itemData.PeriodStartDate:dd/MM/yyyy} - {itemData.PeriodEndDate:dd/MM/yyyy}";
            sheet.Range(infoRow + 1, 2, infoRow + 1, 5).Merge();
            sheet.Cell(infoRow + 1, 9).Value = "Tồn tối thiểu:";
            sheet.Cell(infoRow + 1, 10).Value = itemData.MinimumStockLevel.HasValue ? itemData.MinimumStockLevel.Value.ToString(GetNumberFormat(itemData.MinimumStockLevel.Value)) : "-";

            var infoLabels = sheet.Range(infoRow, 1, infoRow + 1, 1);
            infoLabels.Style.Font.Bold = true;
            infoLabels.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
            sheet.Range(infoRow, 9, infoRow + 1, 9).Style.Font.Bold = true;
            sheet.Range(infoRow, 9, infoRow + 1, 9).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

            int firstHeaderRow = infoRow + 3;
            sheet.Row(firstHeaderRow).Height = 22;
            for (int i = 0; i < headers.Length; i++) { sheet.Cell(firstHeaderRow, i + 1).Value = headers[i]; }

            var headerRange = sheet.Range(firstHeaderRow, 1, firstHeaderRow, headers.Length);
            headerRange.Style.Font.Bold = true;
            headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            headerRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#DDEBF7");

            int currentRow = firstHeaderRow + 1;
            sheet.Row(currentRow).Height = 18;
            var openingStockRange = sheet.Range(currentRow, 1, currentRow, headers.Length);
            openingStockRange.Style.Font.Italic = true;
            openingStockRange.Style.Font.Bold = true;
            openingStockRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            sheet.Range(currentRow, 1, currentRow, headers.Length - 2).Merge().SetValue("Tồn đầu kỳ").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            FormatCellAsNumber(sheet.Cell(currentRow, headers.Length - 1), itemData.OpeningStockQuantity);
            currentRow++;

            foreach (var trans in itemData.Transactions)
            {
                // *** BỔ SUNG: Thêm màu nền chi tiết cho dòng giao dịch ***
                var rowColor = XLColor.White;
                switch (trans.TransactionType)
                {
                    case AppDbContext.InventoryTransactionType.Purchase:
                    case AppDbContext.InventoryTransactionType.InitialStock:
                        rowColor = XLColor.FromHtml("#E7F3E7"); // Xanh lá cây nhạt
                        break;
                    case AppDbContext.InventoryTransactionType.AdjustmentIn:
                        rowColor = XLColor.FromHtml("#EBF5FF"); // Xanh da trời nhạt
                        break;
                    case AppDbContext.InventoryTransactionType.SaleConsumption:
                        rowColor = XLColor.FromHtml("#FFF0F0"); // Hồng nhạt
                        break;
                    case AppDbContext.InventoryTransactionType.AdjustmentOut:
                        rowColor = XLColor.FromHtml("#FFF9E6"); // Vàng kem
                        break;
                }

                if (!rowColor.Equals(XLColor.White)) // So sánh struct màu đúng cách
                {
                    sheet.Range(currentRow, 1, currentRow, headers.Length).Style.Fill.BackgroundColor = rowColor;
                }

                var dataRow = sheet.Row(currentRow);
                dataRow.Cell(1).SetValue(trans.TransactionDate).Style.DateFormat.Format = "dd/MM/yyyy HH:mm";
                dataRow.Cell(2).SetValue(trans.TransactionId);
                dataRow.Cell(3).SetValue(trans.TransactionTypeDisplay);
                dataRow.Cell(4).SetValue(trans.UserName);
                dataRow.Cell(5).SetValue(trans.SupplierName);
                dataRow.Cell(13).SetValue(trans.Notes);

                fillNullable(dataRow.Cell(6), trans.QuantityIn, true);
                fillNullable(dataRow.Cell(8), trans.ValueIn, false);
                fillNullable(dataRow.Cell(9), trans.QuantityOut, true);
                fillNullable(dataRow.Cell(10), trans.UnitPriceOut, false);
                fillNullable(dataRow.Cell(11), trans.ValueOut, false);
                FormatCellAsNumber(dataRow.Cell(12), trans.StockAfterTransaction);

                var unitPriceInCell = dataRow.Cell(7);
                if (trans.TransactionType == AppDbContext.InventoryTransactionType.SaleConsumption)
                {
                    fillNullable(unitPriceInCell, trans.CostPriceOut, false);
                }
                else
                {
                    fillNullable(unitPriceInCell, trans.UnitPriceIn, false);
                }
                currentRow++;
            }

            sheet.Row(currentRow).Height = 22;
            var summaryRange = sheet.Range(currentRow, 1, currentRow, headers.Length);
            summaryRange.Style.Font.Bold = true;
            summaryRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#FFF2CC");
            summaryRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            var summaryLabelCell = summaryRange.Cell(1, 1);
            summaryLabelCell.Value = "Tổng cộng phát sinh";
            sheet.Range(currentRow, 1, currentRow, 5).Merge().Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            FormatCellAsNumber(sheet.Cell(currentRow, 6), itemData.TotalQuantityIn);
            sheet.Cell(currentRow, 8).SetValue(itemData.TotalValueIn).Style.NumberFormat.Format = formatVND;
            FormatCellAsNumber(sheet.Cell(currentRow, 9), itemData.TotalQuantityOut);
            sheet.Cell(currentRow, 11).SetValue(itemData.TotalValueOut).Style.NumberFormat.Format = formatVND;
            currentRow++;

            sheet.Row(currentRow).Height = 18;
            var closingRange = sheet.Range(currentRow, 1, currentRow, headers.Length);
            closingRange.Style.Font.Bold = true;
            closingRange.Style.Font.Italic = true;
            closingRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            var closingLabelCell = sheet.Cell(currentRow, 1);
            closingLabelCell.Value = "Tồn cuối kỳ";
            sheet.Range(currentRow, 1, currentRow, headers.Length - 2).Merge().Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            FormatCellAsNumber(sheet.Cell(currentRow, headers.Length - 1), itemData.ClosingStockQuantity);
            currentRow++;

            var tableRange = sheet.Range(firstHeaderRow, 1, currentRow - 1, headers.Length);
            tableRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            tableRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;

            tableRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            //tableRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;

            sheet.Column(2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            sheet.Column(3).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            sheet.Column(4).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            sheet.Column(5).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            sheet.Column(6).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            sheet.Column(7).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            sheet.Column(8).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            sheet.Column(9).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            sheet.Column(10).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            sheet.Column(11).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            sheet.Column(12).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            sheet.Range(firstHeaderRow + 1, 6, currentRow - 1, 12).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

            sheet.Column(3).Style.Alignment.WrapText = true;
            sheet.Column(4).Style.Alignment.WrapText = true;
            sheet.Column(5).Style.Alignment.WrapText = true;
            sheet.Column(13).Style.Alignment.WrapText = true;
            sheet.Column(13).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

            sheet.Column(1).Width = 18;
            sheet.Column(2).Width = 10;
            sheet.Column(3).Width = 18;
            sheet.Column(4).Width = 18;
            sheet.Column(5).Width = 18;
            sheet.Column(6).Width = 12;
            sheet.Column(7).Width = 15;
            sheet.Column(8).Width = 15;
            sheet.Column(9).Width = 12;
            sheet.Column(10).Width = 15;
            sheet.Column(11).Width = 15;
            sheet.Column(12).Width = 12;
            sheet.Column(13).Width = 35;
        }


        private string GetNumberFormat(decimal value) => value == Math.Truncate(value) ? "#,##0" : "#,##0.###";

        private void FormatCellAsNumber(IXLCell cell, decimal value)
        {
            cell.SetValue(value);
            if (value % 1 == 0) cell.Style.NumberFormat.Format = "#,##0";
            else cell.Style.NumberFormat.Format = "#,##0.####";
        }

        private void fillNullable(IXLCell cell, decimal? val, bool isQuantity)
        {
            if (val.HasValue)
            {
                if (isQuantity) FormatCellAsNumber(cell, val.Value);
                else cell.SetValue(val.Value).Style.NumberFormat.Format = "#,##0";
            }
            else cell.SetValue("-");
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
                // Trả về một PartialView với model rỗng để hiển thị thông báo "Không có dữ liệu"
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

                var lastPurchaseBefore = transactionsBeforePeriod
                    .Where(t => t.QuantityChanged > 0 && t.UnitPrice.HasValue && t.UnitPrice > 0)
                    .OrderByDescending(t => t.TransactionDate).ThenByDescending(t => t.Id)
                    .FirstOrDefault();
                decimal openingStockUnitPrice = lastPurchaseBefore?.UnitPrice ?? ingredient.LastPurchasePrice;
                decimal openingStockValue = openingStockQuantity * openingStockUnitPrice;

                // Lấy giao dịch trong kỳ
                var transactionsInPeriod = await _context.InventoryTransactions
                    .Where(t => t.IngredientId == ingredient.Id && t.TransactionDate >= fromDate && t.TransactionDate <= reportTrueEndDate)
                    .Include(t => t.Batch) // Include Batch để lấy giá vốn chính xác
                    .ToListAsync();

                // Tính toán các giá trị trong kỳ
                decimal periodQuantityIn = transactionsInPeriod.Where(t => t.QuantityChanged > 0).Sum(t => t.QuantityChanged);
                decimal periodValueIn = transactionsInPeriod.Where(t => t.QuantityChanged > 0).Sum(t => t.TotalPrice ?? (t.QuantityChanged * (t.UnitPrice ?? 0)));

                decimal periodQuantityOut = transactionsInPeriod.Where(t => t.QuantityChanged < 0).Sum(t => -t.QuantityChanged);

                // 1. Tính tổng giá trị XUẤT KHO theo GIÁ VỐN (COGS)
                decimal periodValueOut_COGS = transactionsInPeriod
                    .Where(t => t.QuantityChanged < 0)
                    .Sum(t => Math.Abs(t.QuantityChanged) * (t.Batch?.PurchasePrice ?? ingredient.LastPurchasePrice));

                // 2. Tính tổng DOANH THU từ các giao dịch xuất bán hàng
                decimal periodRevenue = transactionsInPeriod
                    .Where(t => t.TransactionType == InventoryTransactionType.SaleConsumption)
                    .Sum(t => t.TotalPrice ?? 0);

                // 3. Tính toán giá trị tồn cuối kỳ CHÍNH XÁC theo giá vốn
                decimal closingStockQuantity = openingStockQuantity + periodQuantityIn - periodQuantityOut;
                decimal closingStockValue = openingStockValue + periodValueIn - periodValueOut_COGS;

                // 4. Tính toán LỢI NHUẬN
                decimal periodProfit = periodRevenue - periodValueOut_COGS;

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
                    PeriodValueOut = periodValueOut_COGS,
                    PeriodRevenue = periodRevenue,
                    PeriodProfit = periodProfit, // Gán giá trị lợi nhuận đã tính
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

            // *** SỬA ĐỔI: Thêm .Include(t => t.Batch) để lấy được thông tin lô hàng ***
            var transactionsInPeriod = await _context.InventoryTransactions
                .Include(t => t.User)
                .Include(t => t.Supplier)
                .Include(t => t.Batch) // <-- THÊM DÒNG NÀY ĐỂ TRUY VẤN KÈM BATCH
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
                    transVM.ValueIn = trans.TotalPrice ?? (trans.QuantityChanged * (trans.UnitPrice ?? 0));
                }
                else // Giao dịch xuất (bán hàng, điều chỉnh giảm)
                {
                    transVM.QuantityOut = -trans.QuantityChanged;

                    // Đối với xuất bán hàng, UnitPriceOut là GIÁ BÁN
                    // Đối với điều chỉnh giảm, UnitPriceOut là GIÁ VỐN
                    transVM.UnitPriceOut = trans.UnitPrice;
                    transVM.ValueOut = trans.TotalPrice;

                    // *** SỬA ĐỔI: Luôn lấy giá vốn từ lô hàng cho mọi giao dịch xuất ***
                    transVM.CostPriceOut = trans.Batch?.PurchasePrice; // <-- GÁN GIÁ VỐN TỪ LÔ HÀNG
                }

                currentStockForDetail += trans.QuantityChanged;
                transVM.StockAfterTransaction = currentStockForDetail;
                detailItem.Transactions.Add(transVM);
            }

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

        // Thêm vào trong #region Dashboard API hoặc một region mới
        [HttpGet]
        public async Task<IActionResult> GetBatchesForIngredient(int ingredientId)
        {
            // BƯỚC 1: Lấy dữ liệu THÔ từ database vào một đối tượng tạm
            // Câu Select này rất đơn giản, Entity Framework có thể dịch sang SQL dễ dàng.
            var rawBatches = await _context.InventoryBatches
                .Where(b => b.IngredientId == ingredientId && b.IsActive && b.Quantity > 0)
                .OrderBy(b => b.PurchaseDate)
                .Select(b => new
                {
                    b.Id,
                    b.Quantity,
                    b.PurchasePrice,
                    b.ExpiryDate
                })
                .ToListAsync(); // <-- Thực thi câu lệnh SQL và lấy kết quả về bộ nhớ

            // BƯỚC 2: Định dạng dữ liệu TRONG BỘ NHỚ C#
            // Lúc này, rawBatches là một List bình thường, ta có thể dùng Select với logic phức tạp
            var formattedBatches = rawBatches.Select(b => {
                var culture = new CultureInfo("vi-VN");
                string formattedQty;

                // Kiểm tra xem số lượng có phải là số nguyên không
                if (b.Quantity == Math.Truncate(b.Quantity))
                {
                    // Nếu là số nguyên, định dạng với dấu phân cách hàng nghìn
                    formattedQty = b.Quantity.ToString("N0", culture);
                }
                else
                {
                    // Nếu là số thập phân, định dạng rồi cắt bỏ số 0 thừa
                    formattedQty = b.Quantity.ToString("N4", culture).TrimEnd('0').TrimEnd(culture.NumberFormat.NumberDecimalSeparator[0]);
                }

                // Định dạng giá
                string formattedPrice = b.PurchasePrice.ToString("N0", culture);

                // Trả về đối tượng cuối cùng cho client
                return new
                {
                    id = b.Id,
                    text = $"Lô #{b.Id} | Tồn: {formattedQty} | Giá: {formattedPrice}đ" + (b.ExpiryDate.HasValue ? " | HSD: " + b.ExpiryDate.Value.ToString("dd/MM/yyyy") : ""),
                    price = b.PurchasePrice
                };
            }).ToList(); // .ToList() để thực thi việc định dạng

            return Ok(formattedBatches);
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
        // POST: /Admin/Inventory/CreateInventoryTransaction
        [HttpPost]
        public async Task<IActionResult> CreateInventoryTransaction([FromBody] InventoryTransactionVM model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(new { success = false, errors = ModelState.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Errors.Select(e => e.ErrorMessage).ToArray()) });
            }

            var ingredient = await _context.Ingredients.FindAsync(model.IngredientId);
            if (ingredient == null)
            {
                return BadRequest(new { success = false, message = "Nguyên vật liệu không hợp lệ." });
            }

            var currentUser = await _userManager.GetUserAsync(User);
            var transaction = new InventoryTransaction
            {
                IngredientId = model.IngredientId,
                TransactionType = model.TransactionType,
                TransactionDate = DateTime.Now,
                UserId = currentUser.Id,
                Notes = model.Notes
            };

            switch (model.TransactionType)
            {
                case InventoryTransactionType.Purchase:
                case InventoryTransactionType.InitialStock:
                    if (!model.UnitPrice.HasValue || model.UnitPrice.Value <= 0)
                        return BadRequest(new { success = false, message = "Đơn giá là bắt buộc cho việc nhập hàng." });

                    var newBatch = new InventoryBatch
                    {
                        IngredientId = model.IngredientId,
                        Quantity = model.Quantity,
                        PurchasePrice = model.UnitPrice.Value,
                        PurchaseDate = DateTime.Now,
                        BatchNumber = $"PO-{DateTime.Now.Ticks}"
                    };
                    _context.InventoryBatches.Add(newBatch);

                    transaction.QuantityChanged = model.Quantity;
                    transaction.UnitPrice = model.UnitPrice;
                    transaction.TotalPrice = model.Quantity * model.UnitPrice.Value;
                    transaction.Batch = newBatch;

                    // *** SỬA LỖI: Bổ sung dòng gán SupplierId ***
                    if (model.TransactionType == InventoryTransactionType.Purchase)
                    {
                        transaction.SupplierId = model.SupplierId;
                    }

                    // Cập nhật tồn kho và giá vốn cuối cùng
                    ingredient.CurrentStockLevel += model.Quantity;
                    ingredient.LastPurchasePrice = model.UnitPrice.Value;
                    ingredient.UpdatedAt = DateTime.Now;
                    break;

                // --- Các case còn lại giữ nguyên ---
                case InventoryTransactionType.AdjustmentIn:
                case InventoryTransactionType.AdjustmentOut:
                    if (!model.BatchId.HasValue)
                        return BadRequest(new { success = false, message = "Vui lòng chọn lô hàng để thực hiện giao dịch." });
                    var batchForAdjustment = await _context.InventoryBatches.FindAsync(model.BatchId.Value);
                    if (batchForAdjustment == null || batchForAdjustment.IngredientId != model.IngredientId)
                        return BadRequest(new { success = false, message = "Lô hàng không hợp lệ." });

                    transaction.BatchId = batchForAdjustment.Id;
                    transaction.UnitPrice = batchForAdjustment.PurchasePrice;

                    if (model.TransactionType == InventoryTransactionType.AdjustmentIn)
                    {
                        batchForAdjustment.Quantity += model.Quantity;
                        transaction.QuantityChanged = model.Quantity;
                        transaction.TotalPrice = model.Quantity * batchForAdjustment.PurchasePrice;
                        ingredient.CurrentStockLevel += model.Quantity;
                    }
                    else // AdjustmentOut
                    {
                        if (batchForAdjustment.Quantity < model.Quantity)
                            return BadRequest(new { success = false, message = $"Số lượng trong lô không đủ. Tồn kho của lô chỉ còn: {batchForAdjustment.Quantity}." });

                        batchForAdjustment.Quantity -= model.Quantity;
                        transaction.QuantityChanged = -model.Quantity;
                        transaction.TotalPrice = -model.Quantity * batchForAdjustment.PurchasePrice;
                        ingredient.CurrentStockLevel -= model.Quantity;
                    }
                    if (batchForAdjustment.Quantity == 0) batchForAdjustment.IsActive = false;
                    ingredient.UpdatedAt = DateTime.Now;
                    break;

                case InventoryTransactionType.SaleConsumption:
                    if (!model.BatchId.HasValue)
                        return BadRequest(new { success = false, message = "Vui lòng chọn lô hàng để bán." });
                    if (!model.UnitPrice.HasValue || model.UnitPrice.Value < 0)
                        return BadRequest(new { success = false, message = "Vui lòng nhập đơn giá bán hợp lệ." });

                    var batchForSale = await _context.InventoryBatches.FindAsync(model.BatchId.Value);
                    if (batchForSale == null || batchForSale.IngredientId != model.IngredientId)
                        return BadRequest(new { success = false, message = "Lô hàng không hợp lệ." });

                    if (batchForSale.Quantity < model.Quantity)
                        return BadRequest(new { success = false, message = $"Số lượng trong lô không đủ. Tồn kho của lô chỉ còn: {batchForSale.Quantity}." });

                    transaction.BatchId = batchForSale.Id;
                    transaction.UnitPrice = model.UnitPrice.Value;
                    transaction.QuantityChanged = -model.Quantity;
                    transaction.TotalPrice = model.Quantity * model.UnitPrice.Value;

                    batchForSale.Quantity -= model.Quantity;
                    ingredient.CurrentStockLevel -= model.Quantity;
                    if (batchForSale.Quantity == 0) batchForSale.IsActive = false;
                    ingredient.UpdatedAt = DateTime.Now;
                    break;

                default:
                    return BadRequest(new { success = false, message = "Loại giao dịch không được hỗ trợ." });
            }

            _context.InventoryTransactions.Add(transaction);
            await _context.SaveChangesAsync();

            var resultData = new
            {
                success = true,
                message = "Tạo phiếu kho thành công!",
                updatedIngredient = new
                {
                    id = ingredient.Id,
                    currentStockLevel = ingredient.CurrentStockLevel
                }
            };

            return Ok(resultData);
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