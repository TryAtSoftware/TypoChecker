# TypoChecker

TypoChecker is a spelling mistake finder for Bulgarian words. It extracts words from PDFs and images, returning the modified PDFs and images with spelling errors marked in red and unreadable words marked in yellow.

## Setup

### 1. Prerequisites

Before you begin, make sure you have the following:

- [ ] .NET SDK installed
- [ ] Azure Document Intelligence API key
- [ ] Azure Document Intelligence endpoint

### 2. Configuration

Create a `appsettings.local.json` file in the `/src/API` directory. Add the following content, replacing `"your API key"` and `"your azure document intelligence endpoint"` with your actual Azure Document Intelligence API key and endpoint:

```json
{
  "DocumentIntelligence": {
    "APIKey": "your API key",
    "Endpoint": "your azure document intelligence endpoint"
  }
}
```

### 3. Build and Run

Now you can build and run the TypoChecker project:

```bash
cd src/API
dotnet build
dotnet run
```

This will start the TypoChecker API.

## How to Use

After compiling and running the API, you can use the following endpoints to process PDFs and images:

### Process PDFs

Send a POST request to https://localhost:7055/api/check_pdfs with the PDF file(s) you want to process.

Example using cURL:
```bash
curl -X POST -H "Content-Type: application/json" -d '{"fileType": "pdf", "fileContent": "<base64-encoded-pdf-content>"}' https://localhost:7055/api/check_pdfs
```
Response:

Modified PDFs with spelling errors marked and a stats.json file containing all the statistics for the files sent.