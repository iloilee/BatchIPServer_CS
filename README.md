# BatchIPServer (C# WinForms - .NET Framework 4.8)

Tool cấu hình IP card mạng hàng loạt cho **Server Phòng Net** trên Windows (10/11, Windows Server 2016/2019/2022).

## Tính năng chính
- Tự động lọc card vật lý, ưu tiên hiển thị card `Up` và tốc độ cao (`1 Gbps` / `10 Gbps`), hỗ trợ đổi thứ tự và nháy cổng nhận diện NIC.
- Nhận diện card **Onboard** và hiển thị thông báo riêng, kèm nút mở nhanh `ncpa.cpl`.
- Đặt IP hàng loạt theo dải (`192.168.1.`, `192.168.254.` hoặc tự nhập) với preset tăng dần (`.200, .201...`) và giảm dần (`.253, .252...`).
- Đổi tên card theo IP kèm xoay vòng tiền tố (`z` → `x` → `c`...) và đổi tên 2 pha chống lỗi trùng tên Windows.
- Hỗ trợ **1 hoặc 2 Gateway** linh hoạt (chung 1 Gateway, chỉ card đầu, chia nửa, xen kẽ, hoặc chỉnh tay từng card + `Metric`).
- Cấu hình DNS mặc định `8.8.8.8` / `8.8.4.4`, kiểm tra trùng IP bất đồng bộ (`Ping`).
- Tùy chọn tối ưu server phòng net: xóa IP/Gateway cũ, tắt **NetBIOS over TCP/IP**, tắt **File and Printer Sharing**, tắt **QoS Packet Scheduler**, tắt **IPv6**, tắt tiết kiệm điện NIC.
- Tự động sao lưu cấu hình trước khi Apply và hỗ trợ **1-click Rollback**.

## Tải nhanh (GitHub Releases)
- Tải file chạy đơn lẻ **`BatchIPServer.exe`** mới nhất tại trang [Releases](https://github.com/iloilee/BatchIPServer_CS/releases/latest) (chỉ 1 file duy nhất, chạy trực tiếp với quyền Administrator).

## Yêu cầu & Build
- **Visual Studio** (hỗ trợ `.NET Framework 4.8` Desktop Development).
- Mở `BatchIPServer.sln` → chọn cấu hình **`Release`** (`Any CPU`) → **Build Solution (`Ctrl + Shift + B`)**.
- File chạy đơn lẻ đầu ra nằm tại `BatchIPServer\bin\Release\BatchIPServer.exe`.

## Phát hành bản mới tự động (GitHub Actions)
Mỗi khi tạo và push tag dạng `v*` (ví dụ `git tag v1.0.1 && git push origin v1.0.1`), workflow `.github/workflows/release.yml` sẽ tự động build bản `Release` trên `windows-latest` và đính kèm `BatchIPServer.exe` lên GitHub Releases.
