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

#### Using cURL:
```bash
curl -X POST -H "Content-Type: multipart/form-data" -F "file1=@\"path\to\file1_with_latinic_alphabet_name.pdf\"" -F "file2=@\"path\to\file2_with_latinic_alphabet_name.pdf\"" https://localhost:7055/api/check_pdfs -o result.zip
```
#### Response:

After the process has completed the results.zip file will be saved wherever you launched the command from. Results.zip contains all the modified PDFs with spelling errors and unknown words marked and a stats.json file containing all the statistics for the files sent.

#### Using Postman (or another third party tool):
1. Open Postman and create a new request.
2. Set the request type to POST.
3. Enter the API endpoint: https://localhost:7055/api/check_pdfs.
4. Switch to the Body tab.
5. Select form-data as the body type.
6. Add the files:
    Set the Key as file1 (or any key you prefer).
    Set the Type as File from the dropdown on the right of the Key.
    For Value, select the file by clicking on the "Choose Files" button and select your PDF file.
7. Repeat the process for additional files.
8. Click on the Send and Download button from the dropdown next to the Send button.
![Screenshot_1](https://github.com/TryAtSoftware/TypoChecker/assets/121127640/f7b51f7f-05d5-48b0-b1af-2dfbaa293cbb)
#### Response:

After the process has completed you will get a prompt for where to save the results.zip file. Results.zip contains all the modified PDFs with spelling errors and unknown words marked and a stats.json file containing all the statistics for the files sent.

### Process Images

Send a POST request to https://localhost:7055/api/check_imgs with the image file(s) you want to process.

#### Example using cURL:
```bash
curl -X POST -H "Content-Type: multipart/form-data" -F "file1=@\"path\to\image1_with_english_alphabet_name.jpg\"" -F "file2=@\"path\to\image2_with_english_alphabet_name.jpg\"" https://localhost:7055/api/check_imgs -o result.zip
```
#### Response:

After the process has completed the results.zip file will be saved wherever you launched the command from. Results.zip contains all the modified images with spelling errors and unknown words marked and a stats.json file containing all the statistics for the files sent.

#### Using Postman (or another third party tool):
1. Open Postman and create a new request.
2. Set the request type to POST.
3. Enter the API endpoint: https://localhost:7055/api/check_imgs.
4. Switch to the Body tab.
5. Select form-data as the body type.
6. Add the files:
    * Set the Key as file1 (or any key you prefer).
    * Set the Type as File from the dropdown menu on the right of the Key.
    * For Value, select the file by clicking on the "Choose Files" button and select your image.
7. Repeat step 6 for additional files.
8. Click on the Send and Download button from the dropdown menu next to the Send button.
![Screenshot_2](https://github.com/TryAtSoftware/TypoChecker/assets/121127640/4137470f-b6f2-47c4-b922-635018629fe8)
#### Response:

After the process has completed you will get a prompt for where to save the results.zip file. Results.zip contains all the modified images with spelling errors and unknown words marked and a stats.json file containing all the statistics for the files sent.