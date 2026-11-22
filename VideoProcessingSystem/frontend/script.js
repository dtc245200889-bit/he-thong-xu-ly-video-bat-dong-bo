const uploadForm = document.querySelector("#upload-form");
const videoFileInput = document.querySelector("#video-file");
const messageElement = document.querySelector("#message");
const videoListElement = document.querySelector("#video-list");
const refreshButton = document.querySelector("#refresh-button");

let refreshTimer = null;
let isLoadingVideos = false;

function showMessage(message, type = "") {
    messageElement.textContent = message;
    messageElement.className = `message ${type}`.trim();
}

async function readResponse(response) {
    const text = await response.text();
    let body = null;

    if (text) {
        try {
            body = JSON.parse(text);
        } catch {
            body = { detail: text };
        }
    }

    if (!response.ok) {
        throw new Error(
            body?.message
            ?? body?.detail
            ?? body?.title
            ?? `HTTP ${response.status} ${response.statusText}`
        );
    }

    return body;
}

function createElement(tagName, className, text) {
    const element = document.createElement(tagName);
    if (className) {
        element.className = className;
    }
    if (text !== undefined) {
        element.textContent = text;
    }
    return element;
}

function formatDate(value) {
    if (!value) {
        return "—";
    }

    const date = new Date(value);
    return Number.isNaN(date.getTime()) ? value : date.toLocaleString("vi-VN");
}

function statusClass(status) {
    return `status status-${String(status ?? "").toLowerCase()}`;
}

function renderResult(job) {
    if (!job.result) {
        if (job.errorMessage) {
            return createElement("p", "job-meta", `Lỗi: ${job.errorMessage}`);
        }
        return null;
    }

    const result = createElement("div", "result");
    result.append(createElement("p", "", `File kết quả: ${job.result.outputFilePath}`));

    if (job.result.metadataJson) {
        const pre = createElement("pre");
        try {
            pre.textContent = JSON.stringify(JSON.parse(job.result.metadataJson), null, 2);
        } catch {
            pre.textContent = job.result.metadataJson;
        }
        result.append(pre);
    }

    return result;
}

function renderJob(job) {
    const card = createElement("article", "job-card");
    const header = createElement("div", "job-header");
    const details = createElement("div");
    details.append(createElement("h4", "", job.taskType));
    details.append(createElement("p", "job-meta", `Job ID: ${job.id}`));
    header.append(details);
    header.append(createElement("span", statusClass(job.status), job.status));
    card.append(header);

    const progressRow = createElement("div", "progress-row");
    const progress = document.createElement("progress");
    progress.max = 100;
    progress.value = Number(job.progress) || 0;
    progress.setAttribute("aria-label", `Tiến trình ${progress.value}%`);
    progressRow.append(progress);
    progressRow.append(createElement("span", "", `${progress.value}%`));
    card.append(progressRow);

    card.append(createElement(
        "p",
        "job-meta",
        `Tạo lúc ${formatDate(job.createdAtUtc)} · Hoàn tất ${formatDate(job.completedAtUtc)}`
    ));

    const result = renderResult(job);
    if (result) {
        card.append(result);
    }

    return card;
}

function renderVideo(video) {
    const card = createElement("article", "video-card");
    const header = createElement("div", "video-card-header");
    const details = createElement("div");
    details.append(createElement("h3", "", video.fileName));
    details.append(createElement("p", "muted", `Đã tải lên ${formatDate(video.createdAtUtc)}`));
    header.append(details);
    card.append(header);
    card.append(createElement("p", "video-id", `Video ID: ${video.id}`));

    const jobs = Array.isArray(video.jobs) ? video.jobs : [];
    if (jobs.length > 0) {
        const jobList = createElement("div", "job-list");
        jobs.forEach((job) => jobList.append(renderJob(job)));
        card.append(jobList);
    } else {
        card.append(createElement("p", "job-meta", "Video này chưa có job xử lý."));
    }

    const actions = createElement("div", "job-actions");
    const createJobButton = createElement("button", "button button-secondary", "Tạo job giả lập");
    createJobButton.type = "button";
    createJobButton.addEventListener("click", () => createJob(video.id, createJobButton));
    actions.append(createJobButton);
    card.append(actions);

    return card;
}

function updatePolling(videos) {
    const hasActiveJobs = videos.some((video) =>
        (video.jobs ?? []).some((job) => job.status === "Pending" || job.status === "Processing")
    );

    if (hasActiveJobs && refreshTimer === null) {
        refreshTimer = window.setInterval(loadVideos, 2000);
    } else if (!hasActiveJobs && refreshTimer !== null) {
        window.clearInterval(refreshTimer);
        refreshTimer = null;
    }
}

async function loadVideos() {
    if (isLoadingVideos) {
        return;
    }

    isLoadingVideos = true;
    refreshButton.disabled = true;

    try {
        const response = await fetch("/videos");
        const videos = await readResponse(response);
        videoListElement.replaceChildren();

        if (!Array.isArray(videos) || videos.length === 0) {
            videoListElement.append(createElement("p", "empty-state", "Chưa có video. Tải video lên để bắt đầu."));
            updatePolling([]);
            return;
        }

        videos.forEach((video) => videoListElement.append(renderVideo(video)));
        updatePolling(videos);
    } catch (error) {
        videoListElement.replaceChildren(
            createElement("p", "empty-state", `Không tải được danh sách: ${error.message}`)
        );
    } finally {
        isLoadingVideos = false;
        refreshButton.disabled = false;
    }
}

async function createJob(videoId, button) {
    button.disabled = true;
    showMessage("Đang gửi job vào RabbitMQ…");

    try {
        const response = await fetch(`/videos/${encodeURIComponent(videoId)}/jobs`, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ taskType: "SimulatedTranscode" })
        });
        const job = await readResponse(response);
        showMessage(`Đã tạo job ${job.id}. Trạng thái: ${job.status}.`, "success");
        await loadVideos();
    } catch (error) {
        showMessage(`Không tạo được job: ${error.message}`, "error");
    } finally {
        button.disabled = false;
    }
}

uploadForm.addEventListener("submit", async (event) => {
    event.preventDefault();
    const file = videoFileInput.files?.[0];
    if (!file) {
        showMessage("Hãy chọn một file video trước khi tải lên.", "error");
        return;
    }

    const submitButton = uploadForm.querySelector('button[type="submit"]');
    submitButton.disabled = true;
    showMessage("Đang tải file lên…");

    try {
        const formData = new FormData(uploadForm);
        const response = await fetch("/videos", {
            method: "POST",
            body: formData
        });
        const video = await readResponse(response);
        uploadForm.reset();
        showMessage(`Tải lên thành công: ${video.fileName}`, "success");
        await loadVideos();
    } catch (error) {
        showMessage(`Tải lên thất bại: ${error.message}`, "error");
    } finally {
        submitButton.disabled = false;
    }
});

refreshButton.addEventListener("click", loadVideos);
loadVideos();
