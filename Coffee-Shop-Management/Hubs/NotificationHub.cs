﻿// Coffee-Shop-Management/Hubs/NotificationHub.cs
using Microsoft.AspNetCore.SignalR;

namespace Coffee_Shop_Management.Hubs
{
    public class NotificationHub : Hub
    {
        public override async Task OnConnectedAsync()
        {
            // Có thể thêm logic khi một client (trình duyệt của nhân viên) kết nối
            await base.OnConnectedAsync();
        }
    }
}