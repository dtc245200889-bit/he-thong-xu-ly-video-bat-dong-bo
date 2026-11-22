# Hệ thống xử lý video bất đồng bộ

Ứng dụng minh họa giao tiếp bất đồng bộ bằng ASP.NET Core Web API, .NET Worker Service, RabbitMQ, SQLite và lưu file trên máy.

## Thành phần

- `MediaApi`: phục vụ API, nhận upload, lưu video, tạo job và gửi message đến RabbitMQ.
- `MediaWorker`: nhận message, cập nhật tiến trình trong SQLite và ghi file kết quả.
- `frontend`: giao diện HTML/CSS/JavaScript được Media API phục vụ tại cùng địa chỉ.
- `storage/uploads`: file video đã upload.
- `storage/outputs`: file JSON mô tả kết quả xử lý.
- `storage/media.db`: SQLite database, được Media API tạo khi khởi động lần đầu.

## Yêu cầu

- .NET 10 SDK.
- Docker Desktop đang chạy.
- PowerShell trong VS Code.
- Một file video để thử upload. API nhận `.mp4`, `.mov`, `.mkv`, `.avi`, `.webm`, tối đa 100 MB.

## Chạy hệ thống

Mở PowerShell tại thư mục `VideoProcessingSystem`.

### 1. Khởi động RabbitMQ

```powershell
docker compose up -d
docker compose ps
```

Chờ RabbitMQ có trạng thái `healthy`. RabbitMQ Management UI: <http://localhost:15672>  
Tài khoản demo: `video_demo` / `video_demo_password`.

### 2. Khởi động Media API

Mở terminal PowerShell thứ hai tại `VideoProcessingSystem`:

```powershell
dotnet run --project .\MediaApi\MediaApi.csproj --no-launch-profile -- --urls http://localhost:5000
```

API cũng phục vụ giao diện tại <http://localhost:5000/>. Khi khởi động lần đầu, API tạo SQLite database và các thư mục storage.

### 3. Khởi động Media Worker

Mở terminal PowerShell thứ ba tại `VideoProcessingSystem`:

```powershell
dotnet run --project .\MediaWorker\MediaWorker.csproj --no-launch-profile
```

Giữ cả API và Worker đang chạy trong khi sử dụng ứng dụng.

## Thử bằng giao diện

1. Mở <http://localhost:5000/>.
2. Chọn file video rồi tải lên.
3. Tạo job với loại tác vụ `SimulatedTranscode`.
4. Giao diện hiển thị tiến trình và tự làm mới trạng thái đến khi job hoàn tất.
5. Chọn làm mới danh sách để xem video và kết quả đã lưu.

## Thử API bằng PowerShell

Thay đường dẫn ví dụ bên dưới bằng đường dẫn đến file video của bạn:

```powershell
$videoPath = 'C:\Videos\sample.mp4'
$uploadJson = curl.exe --fail-with-body -sS -F "file=@$videoPath" http://localhost:5000/videos
$video = $uploadJson | ConvertFrom-Json
$video
```

Tạo job:

```powershell
$body = @{ taskType = 'SimulatedTranscode' } | ConvertTo-Json
$job = Invoke-RestMethod `
    -Uri "http://localhost:5000/videos/$($video.id)/jobs" `
    -Method Post `
    -ContentType 'application/json' `
    -Body $body
$job
```

Tra cứu trạng thái, video và danh sách video:

```powershell
Invoke-RestMethod -Uri "http://localhost:5000/jobs/$($job.id)"
Invoke-RestMethod -Uri "http://localhost:5000/videos/$($video.id)"
Invoke-RestMethod -Uri 'http://localhost:5000/videos'
```

Job được tạo bất đồng bộ, vì vậy phản hồi tạo job là `202 Accepted`. Gọi `GET /jobs/{id}` nhiều lần để xem `Pending`, `Processing`, rồi `Completed` hoặc `Failed`.

## API

| Phương thức | Endpoint | Mô tả |
|---|---|---|
| `POST` | `/videos` | Upload multipart/form-data, tên field là `file`; trả `201 Created`. |
| `GET` | `/videos` | Lấy danh sách video, bao gồm job và kết quả. |
| `GET` | `/videos/{id}` | Lấy thông tin một video, bao gồm job và kết quả. |
| `POST` | `/videos/{id}/jobs` | Tạo job; JSON body: `{"taskType":"SimulatedTranscode"}`; trả `202 Accepted`. |
| `GET` | `/jobs/{id}` | Tra cứu trạng thái, tiến trình và kết quả job. |

Các lỗi đầu vào phổ biến trả `400 Bad Request`; video/job không tồn tại trả `404 Not Found`; API không gửi được message đến RabbitMQ trả `503 Service Unavailable`.

## Kiểm tra RabbitMQ và dữ liệu

Để thấy message đang chờ trong queue, tạm thời chưa khởi động Worker, upload video và tạo job rồi chạy:

```powershell
docker exec video-processing-rabbitmq rabbitmqctl list_queues -p / name messages_ready messages_unacknowledged
```

Sau đó khởi động Worker. Worker nhận message, cập nhật trạng thái và xác nhận message sau khi xử lý thành công. SQLite nằm tại `storage\media.db`; video và file kết quả nằm trong `storage\uploads` và `storage\outputs`.

## Giới hạn của bản thực hành

`SimulatedTranscode` chỉ mô phỏng xử lý nền: Worker chờ giữa các mốc tiến trình, sau đó ghi JSON mô tả kết quả. Chương trình **không giải mã, tạo thumbnail hoặc chuyển đổi video thật**. Video upload được lưu nguyên file; kết quả mô phỏng được ghi riêng trong `storage/outputs`.

## Dừng RabbitMQ

```powershell
docker compose down
```

Lệnh trên dừng container nhưng giữ lại volume RabbitMQ. Không dùng `docker compose down -v` nếu muốn giữ volume đó.
