using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

var baseAddressText = args.Length > 0
    ? args[0]
    : Environment.GetEnvironmentVariable("MEDIA_API_BASE_URL") ?? "http://localhost:5000";

if (!Uri.TryCreate(baseAddressText, UriKind.Absolute, out var baseAddress)
    || (baseAddress.Scheme != Uri.UriSchemeHttp && baseAddress.Scheme != Uri.UriSchemeHttps))
{
    Console.WriteLine($"Địa chỉ API không hợp lệ: {baseAddressText}");
    return;
}

Guid? requestedVideoId = args.Length > 1 && Guid.TryParse(args[1], out var parsedVideoId)
    ? parsedVideoId
    : null;

using var client = new HttpClient { BaseAddress = baseAddress };
var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

try
{
    await RunAsync();
}
catch (HttpRequestException exception)
{
    Console.WriteLine($"Không thể kết nối tới API tại {baseAddress}: {exception.Message}");
}
catch (JsonException exception)
{
    Console.WriteLine($"Phản hồi API không có định dạng JSON hợp lệ: {exception.Message}");
}
catch (TaskCanceledException exception)
{
    Console.WriteLine($"Yêu cầu tới API hết thời gian chờ: {exception.Message}");
}

async Task RunAsync()
{
    Console.WriteLine($"API: {baseAddress}");
    Console.WriteLine("\nBước 1: GET /videos");

    using var videosResponse = await client.GetAsync("/videos");
    if (!videosResponse.IsSuccessStatusCode)
    {
        await PrintApiErrorAsync(videosResponse);
        return;
    }

    var videos = await videosResponse.Content.ReadFromJsonAsync<List<VideoDto>>(jsonOptions)
        ?? throw new JsonException("API trả về danh sách video rỗng hoặc null.");

    foreach (var video in videos)
    {
        Console.WriteLine(
            $"- {video.FileName} | ID: {video.Id} | Tạo lúc: {video.CreatedAtUtc:g}");
    }

    var selectedVideo = requestedVideoId is null
        ? videos.FirstOrDefault()
        : videos.FirstOrDefault(video => video.Id == requestedVideoId.Value);

    if (videos.Count == 0)
    {
        Console.WriteLine("Chưa có video. Hãy tải video lên bằng frontend để thử tạo job thành công.");
    }
    else if (selectedVideo is null)
    {
        Console.WriteLine($"Không tìm thấy video được yêu cầu: {requestedVideoId}");
    }
    else
    {
        Console.WriteLine("\nBước 2: POST /videos/{videoId}/jobs");
        using var createResponse = await client.PostAsJsonAsync(
            $"/videos/{selectedVideo.Id}/jobs",
            new CreateJobRequest("SimulatedTranscode"),
            jsonOptions);

        if (!createResponse.IsSuccessStatusCode)
        {
            await PrintApiErrorAsync(createResponse);
        }
        else
        {
            if (createResponse.StatusCode != HttpStatusCode.Accepted)
            {
                Console.WriteLine(
                    $"Mã trạng thái không như mong đợi: {(int)createResponse.StatusCode} {createResponse.StatusCode}; API nên trả 202 Accepted.");
            }
            else
            {
                Console.WriteLine("API đã tiếp nhận job: 202 Accepted.");
            }

            var job = await createResponse.Content.ReadFromJsonAsync<JobDto>(jsonOptions)
                ?? throw new JsonException("API trả về job rỗng hoặc null.");
            PrintJob(job);

            Console.WriteLine("\nBước 3: GET /jobs/{jobId}");
            using var jobResponse = await client.GetAsync($"/jobs/{job.Id}");
            if (!jobResponse.IsSuccessStatusCode)
            {
                await PrintApiErrorAsync(jobResponse);
            }
            else
            {
                var currentJob = await jobResponse.Content.ReadFromJsonAsync<JobDto>(jsonOptions)
                    ?? throw new JsonException("API trả về job rỗng hoặc null.");
                PrintJob(currentJob);
            }
        }
    }

    var testVideoId = selectedVideo?.Id ?? Guid.NewGuid();

    Console.WriteLine("\nBước 4: Gửi loại job không được hỗ trợ (mong đợi 400 Bad Request)");
    using (var invalidTaskResponse = await client.PostAsJsonAsync(
        $"/videos/{testVideoId}/jobs",
        new CreateJobRequest("UnsupportedTask"),
        jsonOptions))
    {
        await PrintExpectedErrorAsync(invalidTaskResponse, HttpStatusCode.BadRequest);
    }

    Console.WriteLine("\nGửi request thiếu TaskType (mong đợi 400 Bad Request)");
    using (var missingTaskResponse = await client.PostAsJsonAsync(
        $"/videos/{testVideoId}/jobs",
        new { },
        jsonOptions))
    {
        await PrintExpectedErrorAsync(missingTaskResponse, HttpStatusCode.BadRequest);
    }

    Console.WriteLine("\nTra cứu video không tồn tại (mong đợi 404 Not Found)");
    using (var missingVideoResponse = await client.PostAsJsonAsync(
        $"/videos/{Guid.NewGuid()}/jobs",
        new CreateJobRequest("SimulatedTranscode"),
        jsonOptions))
    {
        await PrintExpectedErrorAsync(missingVideoResponse, HttpStatusCode.NotFound);
    }
}

async Task PrintExpectedErrorAsync(HttpResponseMessage response, HttpStatusCode expectedStatus)
{
    if (response.StatusCode != expectedStatus)
    {
        Console.WriteLine(
            $"Không nhận được mã mong đợi {(int)expectedStatus}; nhận {(int)response.StatusCode} {response.StatusCode}.");
    }

    await PrintApiErrorAsync(response);
}

async Task PrintApiErrorAsync(HttpResponseMessage response)
{
    var body = await response.Content.ReadAsStringAsync();
    var status = $"{(int)response.StatusCode} {response.StatusCode}";

    if (string.IsNullOrWhiteSpace(body))
    {
        Console.WriteLine($"Lỗi HTTP {status}; API không gửi nội dung lỗi.");
        return;
    }

    try
    {
        var error = JsonSerializer.Deserialize<ApiError>(body, jsonOptions);
        if (error is not null)
        {
            Console.WriteLine($"Lỗi HTTP {status} | {error.Code}: {error.Message}");
            return;
        }
    }
    catch (JsonException)
    {
        Console.WriteLine($"Lỗi HTTP {status}; thân phản hồi không phải JSON ApiError: {body}");
        return;
    }

    Console.WriteLine($"Lỗi HTTP {status}; thân phản hồi lỗi rỗng.");
}

static void PrintJob(JobDto job)
{
    Console.WriteLine(
        $"Job {job.Id} | Trạng thái: {job.Status} | Tiến trình: {job.Progress}%");
    if (!string.IsNullOrWhiteSpace(job.ErrorMessage))
    {
        Console.WriteLine($"Thông điệp: {job.ErrorMessage}");
    }
}

sealed record CreateJobRequest(string TaskType);

sealed record VideoDto(Guid Id, string FileName, DateTime CreatedAtUtc);

sealed record JobDto(
    Guid Id,
    Guid VideoId,
    string TaskType,
    string Status,
    int Progress,
    DateTime CreatedAtUtc,
    DateTime? CompletedAtUtc,
    string? ErrorMessage);

sealed record ApiError(string Code, string Message);
