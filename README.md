# backup-dl

[![CodeFactor](https://www.codefactor.io/repository/github/jim60105/backup-dl/badge?style=for-the-badge)](https://www.codefactor.io/repository/github/jim60105/backup-dl)  
![License](https://img.shields.io/github/license/jim60105/backup-dl?style=for-the-badge)
![.NET Core](https://img.shields.io/static/v1?style=for-the-badge&message=.NET+Core&color=512BD4&logo=.NET&logoColor=FFFFFF&label=)
![Microsoft Azure](https://img.shields.io/static/v1?style=for-the-badge&message=Microsoft+Azure&color=0078D4&logo=Microsoft+Azure&logoColor=FFFFFF&label=)
![Docker](https://img.shields.io/static/v1?style=for-the-badge&message=Docker&color=2496ED&logo=Docker&logoColor=FFFFFF&label=)
![YouTube](https://img.shields.io/static/v1?style=for-the-badge&message=YouTube&color=FF0000&logo=YouTube&logoColor=FFFFFF&label=)

此程式可以檢查Youtube頻道、播放清單，並備份影片至Azure Blob Storage\
這是一支.Net Core Console Application，並包裝為Linux Container\
整合入我的「Youtube錄影伺服器 (Docker Compose)」，可搭配使用

> [琳的備忘手札 [Docker] Youtube直播錄影伺服器建置](https://blog.maki0419.com/2020/11/docker-youtube-dl-auto-recording-live-dl.html)

## 下載

``` bash
docker pull ghcr.io/jim60105/backup-dl:latest
```

Also available at [quay.io](https://quay.io/repository/jim60105/backup-dl?tab=tags)

## 參數

### 環境變數

- `AZURE_STORAGE_CONNECTION_STRING_VTUBER`: Azure Blob Storage的連接字串
- `CHANNELS_IN_ARRAY`: 欲下載的頻道網址，以陣列傳入
- `MAX_DOWNLOAD`: (可選)每次執行的最大下載數量，預設為10
- `DATE_BEFORE`: (可選)僅下載幾日前的影片，以避免影片尚未完成轉檔
- `FORMAT`: (可選)自訂欲下載的格式，預設為`(bv*+ba/b)[protocol^=http][protocol!*=dash]`，請參考[yt-dlp說明文件](https://github.com/yt-dlp/yt-dlp#format-selection)
- `SCYNCHRONOUS`: (可選)以同步的方式執行程式，請只在單執行緒機器使用

### Volume Binding

- `cookies.txt:/app/cookies.txt` 若bind入，則會在下載時用做登入憑證

## 功能特性

### 影片下載

- 底層使用[yt-dlp](https://github.com/yt-dlp/yt-dlp)做下載 (別人fork youtube-dl出來擴充的專案)
- 使用archive.txt做記錄，不重覆下載影片
- 可傳入多個Youtube頻道，或是Youtube播放清單
- 下載非DASH影片\
([DASH的解釋](https://zh.wikipedia.org/wiki/%E5%9F%BA%E4%BA%8EHTTP%E7%9A%84%E5%8A%A8%E6%80%81%E8%87%AA%E9%80%82%E5%BA%94%E6%B5%81)，簡單來說就是專給網路串流使用的技術格式，而這通常不是最好的影片)
- 可透過環境變數設定下載格式
- 可限制每次執行的最大下載數量
- 限制下載一日前的影片，避免下載到直播中和未轉檔完的影片

### 影片後處理

- 使用ffmpeg做後處理
- 內嵌封面縮圖
- 將影片描述寫入metadata
- 影片統一打包為mkv

### 上傳

- 上傳至Azure Blob Storage
- 影片上傳為封存存取層 ([存取層的官方說明文件](https://docs.microsoft.com/zh-tw/azure/storage/blobs/storage-blob-storage-tiers))

### 其它

- 這是.Net Core Console Application，打包為Linux Container
- 採用非同步設計
        - 每載完一部影片就會進入後處理&上傳程序
        - 多執行緒併行，最大化使用你的效能資源
        - 每片上傳完成後立刻更新archive.txt，不會因中斷container而遺失進度
- 程式重啟後重傳處理
        - 程式啟動時會檢查是否有前次已下載，但未上傳完成的mkv暫存檔案。若有就直接進入上傳

## 使用方式

### Docker

- 在本機環境變數中儲存connection string，命名為「AZURE_STORAGE_CONNECTION_STRING_VTUBER」\
連接字串的說明詳見[官方說明文件](https://docs.microsoft.com/zh-tw/azure/storage/common/storage-account-keys-manage?toc=%2Fazure%2Fstorage%2Fblobs%2Ftoc.json&tabs=azure-portal#view-account-access-keys)
- docker run

``` bash
    docker run 
        --env CHANNELS_IN_ARRAY="[\"https://www.youtube.com/channel/UCBC7vYFNQoGPupe5NxPG4Bw\", \"https://www.youtube.com/channel/UC7XCjKxBEct0uAukpQXNFPw\"]" 
        --env AZURE_STORAGE_CONNECTION_STRING_VTUBER 
        --env MAX_DOWNLOAD="10" 
        jim60105/backup-dl
```

### Kubernetes

```sh
git clone --depth=1 https://github.com/jim60105/backup-dl.git
cd backup-dl/helm
vim values.yaml
kubectl create namespace backupdl
helm install backup-dl . -n backupdl
```

## LICENSE

> Use Xabe.FFmpeg with License Agreement under non-commercial use.  
> Use YoutubeDLSharp under BSD 3-Clause License.  
> Use yt-dlp under Unlicensed License.

<img src="https://github.com/jim60105/backup-dl/assets/16995691/c15741ac-04f9-44e3-b97a-32ecb731c823" alt="open graph" width="200" />

[GNU GENERAL PUBLIC LICENSE Version 3](LICENSE)

This program is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.

This program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU General Public License for more details.

You should have received a copy of the GNU General Public License along with this program. If not, see <https://www.gnu.org/licenses/>.
