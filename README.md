# backup-dl

檢查Youtube頻道，下載非Dash影片，並上傳至Azure Blob Storage\
這是一支 .Net Core Console Application，並包裝為Linux Container，掛在我的個人主機排程docker run

## Setting up

- 在本機環境變數中儲存connection string，命名為「AZURE_STORAGE_CONNECTION_STRING_VTUBER」\
<https://docs.microsoft.com/zh-tw/azure/storage/common/storage-account-keys-manage?toc=%2Fazure%2Fstorage%2Fblobs%2Ftoc.json&tabs=azure-portal#view-account-access-keys>
- docker run

        docker run --rm --env CHANNELS_IN_ARRAY="[\"https://www.youtube.com/channel/UCBC7vYFNQoGPupe5NxPG4Bw\", \"https://www.youtube.com/channel/UC7XCjKxBEct0uAukpQXNFPw\", \"https://www.youtube.com/channel/UCuy-kZJ7HWwUU-eKv0zUZFQ\"]" --env AZURE_STORAGE_CONNECTION_STRING_VTUBER --env Max_Download="10" jim60105/backup-dl
