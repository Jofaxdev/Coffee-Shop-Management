using Coffee_Shop_Management.Areas.Pos.Models;
using Coffee_Shop_Management.Hubs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AppDbContext = Coffee_Shop_Management.Models.AppDbContext;

namespace Coffee_Shop_Management.Areas.Pos.Controllers
{
    [Area("Pos")]
    [Authorize]
    public class HomeController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IHubContext<AppHub> _hubContext;

        public HomeController(AppDbContext context, IHubContext<AppHub> hubContext)
        {
            _context = context;
            _hubContext = hubContext;
        }

        // === HÀM HELPER MỚI: DÙNG ĐỂ PHÁT TÍN HIỆU CẬP NHẬT ĐƠN HÀNG ===
        private async Task BroadcastOrderUpdate(string tableCode)
        {
            var updatedOrder = await _context.Orders
                .Include(o => o.OrderDetails)
                .AsNoTracking() // Dùng AsNoTracking để tăng hiệu suất khi chỉ đọc dữ liệu
                .Where(o => o.TableCode == tableCode && o.StatusPayment != 1)
                .FirstOrDefaultAsync();

            object orderData;
            if (updatedOrder != null)
            {
                orderData = new
                {
                    tableCode = updatedOrder.TableCode,
                    // Ánh xạ dữ liệu chi tiết đơn hàng sang một định dạng đơn giản
                    items = updatedOrder.OrderDetails.Select(od => new OrderItemViewModel
                    {
                        Id = od.ProductId,
                        Quantity = od.Quantity,
                        Price = od.UnitPrice,
                        Note = od.Note
                    }).ToList()
                };
            }
            else
            {
                // Nếu bàn đã được thanh toán hoặc dọn sạch, gửi một danh sách món trống
                orderData = new { tableCode, items = new List<OrderItemViewModel>() };
            }

            // Gửi sự kiện 'OrderUpdated' tới TẤT CẢ các client
            await _hubContext.Clients.All.SendAsync("OrderUpdated", orderData);
        }


        public IActionResult Index()
        {
            return View();
        }

        [HttpGet]
        public async Task<IActionResult> GetPosData()
        {
            var products = await _context.Products
                .Include(p => p.ProductCategorys).ThenInclude(pc => pc.Category)
                .Where(p => p.IsActive && !p.DeleteTemp)
                .Select(p => new ProductViewModel
                {
                    Id = p.ProductId,
                    Name = p.Name,
                    Price = p.UnitPrice,
                    Image = string.IsNullOrEmpty(p.ImageUrl) ? null : "/product/" + p.ImageUrl,
                    Category = p.ProductCategorys.Select(pc => pc.Category.NameCategory).FirstOrDefault() ?? "Chưa phân loại"
                }).ToListAsync();

            var categories = await _context.Categories
                .Where(c => c.IsActive && !c.DeleteTemp)
                .Select(c => c.NameCategory)
                .Distinct()
                .ToListAsync();

            var areas = await _context.Areas
                .Include(a => a.Tables)
                .ThenInclude(t => t.Orders)
                .Where(a => a.IsActive)
                .OrderBy(a => a.DisplayOrder)
                .Select(a => new AreaViewModel
                {
                    Name = a.Name,
                    Tables = a.Tables.Where(t => t.IsActive)
                                     .OrderBy(t => t.DisplayOrder)
                                     .Select(t => new TableViewModel
                                     {
                                         Id = t.TableCode,
                                         Name = t.NameTable,
                                         Status = t.Orders.Any(o => o.StatusPayment != 1) ? "occupied" : "available",
                                         Request = t.Request
                                     }).ToList()
                }).ToListAsync();

            var model = new PosDataViewModel
            {
                Products = products,
                Categories = new List<string> { "Tất cả" }.Concat(categories).ToList(),
                Areas = areas
            };

            return Ok(model);
        }

        [HttpGet]
        public async Task<IActionResult> GetOrder(string tableCode)
        {
            var order = await _context.Orders
                .Include(o => o.OrderDetails)
                .Where(o => o.TableCode == tableCode && o.StatusPayment != 1)
                .OrderByDescending(o => o.CreatedAt)
                .FirstOrDefaultAsync();

            if (order == null)
            {
                return Ok(new OrderViewModel { TableId = tableCode, Items = new List<OrderItemViewModel>(), StatusPayment = 0 });
            }

            return Ok(new OrderViewModel
            {
                TableId = order.TableCode,
                StatusPayment = order.StatusPayment,
                Items = order.OrderDetails.Select(od => new OrderItemViewModel { Id = od.ProductId, Quantity = od.Quantity, Price = od.UnitPrice, Note = od.Note }).ToList()
            });
        }

        [HttpPost]
        public async Task<IActionResult> SaveOrder([FromBody] OrderViewModel model)
        {
            if (model == null || string.IsNullOrEmpty(model.TableId)) return BadRequest("Dữ liệu không hợp lệ.");
            var table = await _context.Tables.FindAsync(model.TableId);
            if (table == null) return NotFound("Không tìm thấy bàn.");

            var order = await _context.Orders.Include(o => o.OrderDetails)
                .Where(o => o.TableCode == model.TableId && o.StatusPayment != 1).FirstOrDefaultAsync();
            var wasAvailable = table.IsAvailable;

            if (!model.Items.Any())
            {
                if (order != null)
                {
                    _context.OrderDetails.RemoveRange(order.OrderDetails);
                    _context.Orders.Remove(order);
                }
                table.IsAvailable = true;
                if (!wasAvailable)
                {
                    await _hubContext.Clients.All.SendAsync("ReceiveTableStatusUpdate", new { tableId = table.TableCode, newStatus = "available" });
                }
            }
            else
            {
                if (order == null)
                {
                    var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                    var userName = User.Identity.Name;
                    var area = await _context.Areas.FindAsync(table.AreaCode);
                    order = new AppDbContext.Order
                    {
                        TableCode = model.TableId,
                        TableName = table.NameTable,
                        AreaName = area?.Name ?? "N/A",
                        CreatedAt = DateTime.Now,
                        StatusPayment = 0,
                        InvoiceCreatorId = userId,
                        InvoiceCreatorName = userName
                    };
                    _context.Orders.Add(order);
                    await _context.SaveChangesAsync();
                }
                order.UpdatedAt = DateTime.Now;
                _context.OrderDetails.RemoveRange(order.OrderDetails);
                decimal totalAmount = 0;
                foreach (var item in model.Items)
                {
                    var product = await _context.Products.FindAsync(item.Id);
                    if (product != null)
                    {
                        _context.OrderDetails.Add(new AppDbContext.OrderDetail
                        {
                            OrderId = order.Id,
                            ProductId = item.Id,
                            NameProduct = product.Name,
                            Quantity = item.Quantity,
                            UnitPrice = item.Price,
                            UnitOfMeasure = product.UnitOfMeasure,
                            TotalPrice = item.Quantity * item.Price,
                            Note = item.Note
                        });
                        totalAmount += item.Quantity * item.Price;
                    }
                }
                order.TotalAmount = totalAmount;
                order.VAT = totalAmount * 0.08m;
                table.IsAvailable = false;
                if (wasAvailable)
                {
                    await _hubContext.Clients.All.SendAsync("ReceiveTableStatusUpdate", new { tableId = table.TableCode, newStatus = "occupied" });
                }
            }
            await _context.SaveChangesAsync();

            // === BROADCAST: Gửi cập nhật chi tiết đơn hàng cho tất cả client ===
            await BroadcastOrderUpdate(model.TableId);

            return Ok(new { success = true, message = "Lưu đơn hàng thành công!" });
        }

        [HttpPost]
        public async Task<IActionResult> Checkout([FromBody] CheckoutVM model)
        {
            var order = await _context.Orders.Include(o => o.OrderDetails)
                .Where(o => o.TableCode == model.TableCode && o.StatusPayment != 1).FirstOrDefaultAsync();
            if (order == null) return NotFound("Không tìm thấy đơn hàng để thanh toán.");

            var table = await _context.Tables.FindAsync(order.TableCode);
            if (table != null) table.IsAvailable = true;

            order.StatusPayment = 1;
            order.CustomerMoney = model.CashReceived;
            order.MoneyToCustomer = model.CashReceived - (order.TotalAmount + order.VAT);
            order.UpdatedAt = DateTime.Now;

            await _context.SaveChangesAsync();
            await _hubContext.Clients.All.SendAsync("ReceiveTableStatusUpdate", new { tableId = table.TableCode, newStatus = "available" });

            // === BROADCAST: Bàn đã thanh toán xong, gửi đơn hàng trống ===
            await BroadcastOrderUpdate(model.TableCode);

            return Ok(new { success = true, message = "Thanh toán thành công!" });
        }

        [HttpPost]
        public async Task<IActionResult> MoveTable([FromBody] MoveTableVM model)
        {
            if (string.IsNullOrEmpty(model.FromTableCode) || string.IsNullOrEmpty(model.ToTableCode)) return BadRequest("Dữ liệu chuyển bàn không hợp lệ.");
            var fromTable = await _context.Tables.FindAsync(model.FromTableCode);
            var toTable = await _context.Tables.FindAsync(model.ToTableCode);
            if (fromTable == null || toTable == null) return NotFound("Bàn không tồn tại.");
            var orderToMove = await _context.Orders.Where(o => o.TableCode == model.FromTableCode && o.StatusPayment != 1).FirstOrDefaultAsync();
            if (orderToMove == null) return BadRequest("Bàn nguồn không có đơn hàng để chuyển.");
            if (_context.Orders.Any(o => o.TableCode == model.ToTableCode && o.StatusPayment != 1)) return BadRequest($"Bàn đích '{toTable.NameTable}' đang có đơn hàng, không thể chuyển.");

            orderToMove.TableCode = toTable.TableCode;
            orderToMove.TableName = toTable.NameTable;
            orderToMove.AreaName = (await _context.Areas.FindAsync(toTable.AreaCode))?.Name ?? "N/A";
            orderToMove.UpdatedAt = DateTime.Now;

            fromTable.IsAvailable = true;
            toTable.IsAvailable = false;

            await _context.SaveChangesAsync();

            await _hubContext.Clients.All.SendAsync("ReceiveTableStatusUpdate", new { tableId = fromTable.TableCode, newStatus = "available" });
            await _hubContext.Clients.All.SendAsync("ReceiveTableStatusUpdate", new { tableId = toTable.TableCode, newStatus = "occupied" });

            // === BROADCAST: Cập nhật cho cả 2 bàn liên quan ===
            await BroadcastOrderUpdate(model.FromTableCode); // Bàn nguồn giờ đã trống
            await BroadcastOrderUpdate(model.ToTableCode);   // Bàn đích có đơn hàng mới

            return Ok(new { success = true, message = $"Đã chuyển đơn từ {fromTable.NameTable} sang {toTable.NameTable}" });
        }

        [HttpPost]
        public async Task<IActionResult> SplitBill([FromBody] SplitBillVM model)
        {
            if (model == null || !model.ItemsToMove.Any()) return BadRequest("Dữ liệu tách hóa đơn không hợp lệ.");

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var sourceOrder = await _context.Orders.Include(o => o.OrderDetails).FirstOrDefaultAsync(o => o.TableCode == model.SourceTableCode && o.StatusPayment != 1);
                if (sourceOrder == null) return NotFound("Không tìm thấy đơn hàng gốc.");

                var destinationTable = await _context.Tables.FindAsync(model.DestinationTableCode);
                if (destinationTable == null) return NotFound("Không tìm thấy bàn đích.");

                var destinationOrder = await _context.Orders.Include(o => o.OrderDetails).FirstOrDefaultAsync(o => o.TableCode == model.DestinationTableCode && o.StatusPayment != 1);

                if (destinationOrder == null)
                {
                    var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                    var userName = User.Identity.Name;
                    destinationOrder = new AppDbContext.Order
                    {
                        TableCode = destinationTable.TableCode,
                        TableName = destinationTable.NameTable,
                        AreaName = (await _context.Areas.FindAsync(destinationTable.AreaCode))?.Name ?? "N/A",
                        CreatedAt = DateTime.Now,
                        StatusPayment = 0,
                        InvoiceCreatorId = userId,
                        InvoiceCreatorName = userName,
                        OrderDetails = new List<AppDbContext.OrderDetail>()
                    };
                    _context.Orders.Add(destinationOrder);
                }

                foreach (var itemToMove in model.ItemsToMove)
                {
                    var product = await _context.Products.FindAsync(itemToMove.Id);
                    if (product == null) continue;

                    var sourceDetail = sourceOrder.OrderDetails.FirstOrDefault(d => d.ProductId == itemToMove.Id && (d.Note ?? string.Empty) == itemToMove.Note);
                    if (sourceDetail == null || sourceDetail.Quantity < itemToMove.Quantity) throw new Exception($"Không đủ số lượng sản phẩm '{product.Name}' để tách.");

                    var destinationDetail = destinationOrder.OrderDetails.FirstOrDefault(d => d.ProductId == itemToMove.Id && (d.Note ?? string.Empty) == itemToMove.Note);

                    if (destinationDetail != null)
                    {
                        destinationDetail.Quantity += itemToMove.Quantity;
                        destinationDetail.TotalPrice = destinationDetail.Quantity * destinationDetail.UnitPrice;
                    }
                    else
                    {
                        destinationOrder.OrderDetails.Add(new AppDbContext.OrderDetail
                        {
                            ProductId = product.ProductId,
                            NameProduct = product.Name,
                            Quantity = itemToMove.Quantity,
                            UnitPrice = itemToMove.Price,
                            TotalPrice = itemToMove.Price * itemToMove.Quantity,
                            Note = itemToMove.Note,
                            UnitOfMeasure = product.UnitOfMeasure
                        });
                    }

                    sourceDetail.Quantity -= itemToMove.Quantity;
                    sourceDetail.TotalPrice = sourceDetail.Quantity * sourceDetail.UnitPrice;
                    if (sourceDetail.Quantity == 0) _context.OrderDetails.Remove(sourceDetail);
                }

                destinationOrder.TotalAmount = destinationOrder.OrderDetails.Sum(d => d.TotalPrice);
                destinationOrder.VAT = destinationOrder.TotalAmount * 0.08m;
                destinationOrder.UpdatedAt = DateTime.Now;

                sourceOrder.TotalAmount = sourceOrder.OrderDetails.Sum(d => d.TotalPrice);
                sourceOrder.VAT = sourceOrder.TotalAmount * 0.08m;
                sourceOrder.UpdatedAt = DateTime.Now;

                bool sourceTableBecomesAvailable = false;
                destinationTable.IsAvailable = false;
                if (!sourceOrder.OrderDetails.Any())
                {
                    _context.Orders.Remove(sourceOrder);
                    var sourceTable = await _context.Tables.FindAsync(model.SourceTableCode);
                    if (sourceTable != null) sourceTable.IsAvailable = true;
                    sourceTableBecomesAvailable = true;
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                await _hubContext.Clients.All.SendAsync("ReceiveTableStatusUpdate", new { tableId = destinationTable.TableCode, newStatus = "occupied" });
                if (sourceTableBecomesAvailable)
                {
                    await _hubContext.Clients.All.SendAsync("ReceiveTableStatusUpdate", new { tableId = model.SourceTableCode, newStatus = "available" });
                }

                // === BROADCAST: Cập nhật cho cả 2 bàn liên quan ===
                await BroadcastOrderUpdate(model.SourceTableCode);
                await BroadcastOrderUpdate(model.DestinationTableCode);

                return Ok(new { success = true, message = "Tách/Gộp món thành công!" });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return StatusCode(500, $"Lỗi nội bộ: {ex.Message}");
            }
        }

        [HttpPost]
        public async Task<IActionResult> MergeTables([FromBody] MergeTablesVM model)
        {
            if (model == null || string.IsNullOrEmpty(model.DestinationTableCode) || !model.SourceTableCodes.Any()) return BadRequest("Dữ liệu gộp bàn không hợp lệ.");
            if (model.SourceTableCodes.Contains(model.DestinationTableCode)) return BadRequest("Bàn nguồn không được trùng với bàn đích.");

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var destinationOrder = await _context.Orders.Include(o => o.OrderDetails).FirstOrDefaultAsync(o => o.TableCode == model.DestinationTableCode && o.StatusPayment != 1);
                var destinationTable = await _context.Tables.FindAsync(model.DestinationTableCode);
                if (destinationTable == null) return NotFound("Không tìm thấy bàn đích.");

                if (destinationOrder == null)
                {
                    var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                    var userName = User.Identity.Name;
                    var area = await _context.Areas.FindAsync(destinationTable.AreaCode);
                    destinationOrder = new AppDbContext.Order { TableCode = destinationTable.TableCode, TableName = destinationTable.NameTable, AreaName = area?.Name ?? "N/A", CreatedAt = DateTime.Now, StatusPayment = 0, InvoiceCreatorId = userId, InvoiceCreatorName = userName, OrderDetails = new List<AppDbContext.OrderDetail>() };
                    _context.Orders.Add(destinationOrder);
                }

                foreach (var sourceCode in model.SourceTableCodes)
                {
                    var sourceOrder = await _context.Orders.Include(o => o.OrderDetails).FirstOrDefaultAsync(o => o.TableCode == sourceCode && o.StatusPayment != 1);
                    if (sourceOrder == null) continue;
                    foreach (var sourceDetail in sourceOrder.OrderDetails)
                    {
                        var destinationDetail = destinationOrder.OrderDetails.FirstOrDefault(d =>
                            d.ProductId == sourceDetail.ProductId &&
                            (d.Note ?? string.Empty) == (sourceDetail.Note ?? string.Empty));

                        if (destinationDetail != null)
                        {
                            destinationDetail.Quantity += sourceDetail.Quantity;
                            destinationDetail.TotalPrice = destinationDetail.Quantity * destinationDetail.UnitPrice;
                        }
                        else
                        {
                            destinationOrder.OrderDetails.Add(new AppDbContext.OrderDetail { ProductId = sourceDetail.ProductId, NameProduct = sourceDetail.NameProduct, Quantity = sourceDetail.Quantity, UnitPrice = sourceDetail.UnitPrice, TotalPrice = sourceDetail.TotalPrice, Note = sourceDetail.Note, UnitOfMeasure = sourceDetail.UnitOfMeasure });
                        }
                    }
                    _context.Orders.Remove(sourceOrder);
                    var sourceTable = await _context.Tables.FindAsync(sourceCode);
                    if (sourceTable != null) sourceTable.IsAvailable = true;
                }

                destinationOrder.TotalAmount = destinationOrder.OrderDetails.Sum(d => d.TotalPrice);
                destinationOrder.VAT = destinationOrder.TotalAmount * 0.08m;
                destinationOrder.UpdatedAt = DateTime.Now;
                destinationTable.IsAvailable = false;
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                await _hubContext.Clients.All.SendAsync("ReceiveTableStatusUpdate", new { tableId = destinationTable.TableCode, newStatus = "occupied" });

                // === BROADCAST: Cập nhật cho tất cả các bàn liên quan ===
                foreach (var sourceCode in model.SourceTableCodes)
                {
                    await _hubContext.Clients.All.SendAsync("ReceiveTableStatusUpdate", new { tableId = sourceCode, newStatus = "available" });
                    await BroadcastOrderUpdate(sourceCode);
                }
                await BroadcastOrderUpdate(model.DestinationTableCode);


                return Ok(new { success = true, message = "Gộp bàn thành công!" });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return StatusCode(500, $"Lỗi nội bộ khi gộp bàn: {ex.Message}");
            }
        }
    }
}