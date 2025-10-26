using UnityEngine;
using UnityEngine.UI;
using System.Text;
using System.Threading.Tasks;
using UnityEngine.Networking;
using GLTFast;
using TMPro;
using System.Globalization;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Collections;
// using Siccity.GLTFUtility; // Assuming you use GLTFUtility for loading GLB


public class CrazyRecipeDemo : MonoBehaviour
{
    [Header("UI")]
    public Button generateButton;

    public TMP_Text outputText;
    private GameObject currentModel;  // reference to the loaded model
    // private bool modelReady = false;  // true if model has loaded successfully
    [Header("Preview UI")]
    public RawImage recipeImagePreview; // assign this in Inspector to display image
    public RawImage previewImageUI; // drag your UI Image from the Inspector

    [Header("Endpoints")]
    private string ollamaEndpoint = "http://localhost:11434/api/chat";
    // private string localDiffusionEndpoint = "http://127.0.0.1:5000/generate";
    private string hyper3dUploadEndpoint = "https://api.hyper3d.com/api/v2/rodin";
    // private string hyper3dDownloadEndpoint = "https://api.hyper3d.com/api/v2/download";
    // private string hyper3dApiBaseUrl =  "https://api.hyper3d.com";

    [Header("Hyper3D API")]
    public string hyper3dStatusEndpoint = "https://api.hyper3d.com/api/v2/status";
    public string hyper3dDownloadEndpoint = "https://api.hyper3d.com/api/v2/download";


    [Header("Keys")]
    public string hyper3dApiKey = ""; // Replace with your key

    private void Start()
    {
        generateButton.onClick.AddListener(() =>
        {
            _ = GenerateAndRenderRecipeAsync(); // fire-and-forget
        });

    }

    // Helper to remove emojis / non-basic Unicode symbols
    private string RemoveEmojis(string text)
    {
        var sb = new StringBuilder();
        foreach (char c in text)
        {
            var category = char.GetUnicodeCategory(c);
            // Only keep standard characters (letters, digits, punctuation, whitespace)
            if (category != System.Globalization.UnicodeCategory.Surrogate &&
                category != System.Globalization.UnicodeCategory.OtherSymbol)
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }
    
    private async Task GenerateAndRenderRecipeAsync()
{
    outputText.text = "Cooking up something insane...";

    string jsonData = "{\"model\":\"llama3.2:latest\",\"messages\":[{\"role\":\"user\",\"content\":\"Generate a crazy, funny recipe using random ingredients. Keep it short (no emojis).\"}]}";

    using (UnityWebRequest llmRequest = new UnityWebRequest(ollamaEndpoint, "POST"))
    {
        byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonData);
        llmRequest.uploadHandler = new UploadHandlerRaw(bodyRaw);
        llmRequest.downloadHandler = new DownloadHandlerBuffer();
        llmRequest.SetRequestHeader("Content-Type", "application/json");

        await llmRequest.SendWebRequestAsync();

        if (llmRequest.result != UnityWebRequest.Result.Success)
        {
            outputText.text = "LLM error: " + llmRequest.error;
            return;
        }

        string llmResponse = llmRequest.downloadHandler.text;
        string recipeText = RemoveEmojis(ExtractContent(llmResponse));

        if (string.IsNullOrWhiteSpace(recipeText) || recipeText == "No content")
        {
            outputText.text = "Failed to generate recipe. Try again.";
            return;
        }

        outputText.text = "Recipe: " + recipeText;

        // Generate 3D model pipeline
        await Generate3DModelAsync(recipeText);
    }
}

private async Task Generate3DModelAsync(string recipeText)
{
    recipeText = RemoveEmojis(recipeText);
    outputText.text += "\nGenerating image from local diffusion model...";
    Debug.Log("Sending prompt to local diffusion server: " + recipeText);

    // 1️⃣ Call local diffusion server
    string diffusionEndpoint = "http://127.0.0.1:5000/generate";
    var requestObj = new DiffusionRequest { prompt = recipeText };
    string jsonPayload = JsonUtility.ToJson(requestObj);

    string base64Image = null;
    using (UnityWebRequest diffusionRequest = new UnityWebRequest(diffusionEndpoint, "POST"))
    {
        diffusionRequest.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonPayload));
        diffusionRequest.downloadHandler = new DownloadHandlerBuffer();
        diffusionRequest.SetRequestHeader("Content-Type", "application/json");

        await diffusionRequest.SendWebRequestAsync();

        if (diffusionRequest.result != UnityWebRequest.Result.Success)
        {
            outputText.text += "\nImage generation failed: " + diffusionRequest.error;
            Debug.LogError("Diffusion generation failed: " + diffusionRequest.error);
            return;
        }

        string response = diffusionRequest.downloadHandler.text;
        var match = Regex.Match(response, "\"image\"\\s*:\\s*\"([^\"]+)\"");
        if (match.Success)
        {
            base64Image = match.Groups[1].Value;
            Debug.Log("Extracted base64 image string, length: " + base64Image.Length);
        }
        else
        {
            outputText.text += "\nNo image received from diffusion server.";
            Debug.LogError("No 'image' field found in response JSON. Full response:\n" + response);
            return;
        }
    }

    // 2️⃣ Display preview in Unity
    byte[] imageBytes = System.Convert.FromBase64String(base64Image);
    Texture2D texture = new Texture2D(2, 2);
    texture.LoadImage(imageBytes);
    if (recipeImagePreview != null)
    {
        recipeImagePreview.texture = texture;
        recipeImagePreview.gameObject.SetActive(true);
    }
    outputText.text += "\nImage generated locally.";

    outputText.text += "\nUploading image + prompt to Hyper3D...";

    texture.LoadImage(imageBytes);
    await UploadImageWithPromptToHyper3D(texture, recipeText);

}

private async Task UploadImageWithPromptToHyper3D(Texture2D texture, string prompt)
{
    outputText.text += "\nUploading image + prompt to Hyper3D...";

    // Encode the texture to PNG
    byte[] imageBytes = texture.EncodeToPNG();

    // Build multipart form
    List<IMultipartFormSection> form = new List<IMultipartFormSection>
    {
        new MultipartFormFileSection("images", imageBytes, "recipe.png", "image/png"),
        new MultipartFormDataSection("prompt", prompt)
    };

    using (UnityWebRequest webRequest = UnityWebRequest.Post(hyper3dUploadEndpoint, form))
    {
        webRequest.SetRequestHeader("Authorization", "Bearer " + hyper3dApiKey);

        await webRequest.SendWebRequestAsync();

        string uploadResponse = webRequest.downloadHandler.text;
        Debug.Log("Hyper3D Upload Response: " + uploadResponse);

        if (webRequest.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError("Hyper3D upload failed. Response: " + uploadResponse);
            outputText.text += "\nHyper3D upload error: " + webRequest.error;
            return;
        }

        // ... (after checking webRequest.result)

        string taskUUID = ExtractTaskUUIDJSON(uploadResponse);
        string subscriptionKey = ExtractSubscriptionKey(uploadResponse); // <-- NEW LINE

        outputText.text += "\nModel task UUID: " + taskUUID;
        outputText.text += "\nPolling Key: " + subscriptionKey; // Added for debugging

        if (string.IsNullOrEmpty(subscriptionKey) || subscriptionKey == "unknown") // <-- CHECK THIS KEY
        {
            Debug.LogError("Hyper3D returned unknown subscription key. Full response: " + uploadResponse);
            outputText.text += "\nError: received unknown subscription key.";
            return;
        }

        // Start polling for 3D model, passing the correct key
        await PollForModelAsync(taskUUID, subscriptionKey); // <-- PASS THE CORRECT KEY
    }
}

public async Task<bool> PollForModelAsync(string taskUUID, string sub_key, int pollIntervalSeconds = 5, int maxAttempts = 25)
{
    outputText.text += "\nPolling Hyper3D for model readiness (waiting for ALL jobs to complete)...";
    // If you were hiding the preview here, make sure it stays commented out:
    // recipeImagePreview.gameObject.SetActive(false); 

    bool modelReady = false;
    int currentAttempt = 0; 

    while (!modelReady && currentAttempt < maxAttempts)
    {
        currentAttempt++;
        
        // Prepare the JSON payload using the subscription key for polling
        string jsonPayload = JsonUtility.ToJson(new Hyper3DPollRequest
        {
            subscription_key = sub_key 
        });

        Debug.Log($"Polling attempt {currentAttempt}/{maxAttempts} for key: {sub_key}");

        using (UnityWebRequest statusRequest = new UnityWebRequest(hyper3dStatusEndpoint, "POST"))
        {
            statusRequest.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonPayload));
            statusRequest.downloadHandler = new DownloadHandlerBuffer();
            statusRequest.SetRequestHeader("Authorization", "Bearer " + hyper3dApiKey);
            statusRequest.SetRequestHeader("Content-Type", "application/json");

            await statusRequest.SendWebRequestAsync();
            
            if (statusRequest.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("Error polling Hyper3D: " + statusRequest.error);
                outputText.text += $"\nPolling error: {statusRequest.error}. Attempting again in {pollIntervalSeconds}s...";
                await Task.Delay(pollIntervalSeconds * 1000); 
                continue; 
            }

            string responseText = statusRequest.downloadHandler.text;
            Debug.Log("Polling response: " + responseText); 

            var statusResp = JsonUtility.FromJson<Hyper3DStatusResponse>(responseText);
            
            // 1. FATAL ERROR CHECK: Check the top-level 'error' field 
            if (!string.IsNullOrEmpty(statusResp.error))
            {
                Debug.LogError($"Polling halted: API Error: {statusResp.error}");
                outputText.text += $"\nError: Hyper3D API reported: {statusResp.error}";
                return false; 
            }
            
            // 2. JOB STATUS CHECK (STRICT: All must be "Done")
            if (statusResp.jobs != null && statusResp.jobs.Length > 0)
            {
                bool allDone = true;
                
                foreach (var job in statusResp.jobs)
                {
                    if (job.status == "Failed") // Immediate failure check
                    {
                        outputText.text += $"\nGeneration FAILED: Job {job.uuid} failed.";
                        Debug.LogError("Hyper3D Generation Failed.");
                        return false; 
                    }
                    
                    if (job.status != "Done") // If any job is Waiting or Generating
                    {
                        allDone = false;
                        break; 
                    }
                }
                
                if (allDone)
                {
                    // Success condition met: All jobs have completed successfully.
                    outputText.text += "\nModel ready! (All jobs completed).";
                    modelReady = true; // Exit loop
                }
            }
            
            // 3. WAIT
            if (!modelReady) 
            {
                outputText.text += $"\nModel not ready yet ({currentAttempt}/{maxAttempts}), waiting {pollIntervalSeconds}s...";
                await Task.Delay(pollIntervalSeconds * 1000); 
            }
        } 
    } 

    // --- FINAL ACTION / TIMEOUT CHECK ---
    if (modelReady)
    {
        // Success! Call the download method using the original top-level taskUUID.
        return await GetDownloadUrlsAndLoadModelAsync(taskUUID); 
    }
    else
    {
        // TIMEOUT CHECK
        outputText.text = $"Model generation timed out after {maxAttempts} attempts ({maxAttempts * pollIntervalSeconds} seconds).";
        Debug.LogError("Model generation timed out.");
        return false;
    }
}

// -------------------
// Download from Hyper3D
// -------------------
private async Task DownloadAndLoadModelAsync(string glbUrl, string previewUrl)
{
    outputText.text += "\nDownloading GLB model...";

    // Load GLB with GLTFast
    await LoadModelFromURLAsync(glbUrl);

    // Download preview image
    using (UnityWebRequest imgRequest = UnityWebRequestTexture.GetTexture(previewUrl))
    {
        imgRequest.SetRequestHeader("Authorization", "Bearer " + hyper3dApiKey); // include your key
        await imgRequest.SendWebRequestAsync();

        if (imgRequest.result == UnityWebRequest.Result.Success)
        {
            Texture2D texture = DownloadHandlerTexture.GetContent(imgRequest);
            // recipeImagePreview.texture = tex;
            // recipeImagePreview.gameObject.SetActive(true); // show only when ready
            // byte[] imageBytes = System.Convert.FromBase64String(base64Image);
            // Texture2D texture = new Texture2D(2, 2);
            // texture.LoadImage(imageBytes);
            if (previewImageUI != null)
            {
                previewImageUI.texture = texture;
                previewImageUI.gameObject.SetActive(true); 
            }

            outputText.text += "\nPreview image loaded!";
        }
        else
        {
            Debug.LogWarning("Failed to download preview image: " + imgRequest.error);
        }
    }
}


    // -------------------
    // Load GLB
    // -------------------
    // private async Task LoadModelFromURLAsync(string url)
    // {
    //     outputText.text += "\nLoading model...";

    //     var gltf = new Siccity.GLTFUtility.Importer(); // Make sure GLTFUtility is installed
    //     bool success = await gltf.Load(url);

    //     if (success)
    //     {
    //         if (currentModel != null) Destroy(currentModel);
    //         currentModel = new GameObject("GeneratedModel");
    //         await gltf.InstantiateMainSceneAsync(currentModel.transform);
    //         currentModel.transform.position = Vector3.zero;
    //         outputText.text += "\nModel loaded!";
    //     }
    //     else
    //     {
    //         outputText.text += "\nFailed to load GLB model.";
    //     }
    // }

private async Task<bool> GetDownloadUrlsAndLoadModelAsync(string taskUUID)
{
    outputText.text += "\nFetching final download URLs...";
    await Task.Delay(2000); // Keep the 2-second buffer for safety
    outputText.text += "\nFetching final download URLs using job UUID: " + taskUUID;

    // Use the subscription key as the payload for the download request
    string jsonPayload = JsonUtility.ToJson(new Hyper3DDownloadRequest 
    {
        // task_uuid = taskUUID
        task_uuid = taskUUID
    });

    using (UnityWebRequest downloadRequest = new UnityWebRequest(hyper3dDownloadEndpoint, "POST"))
    {
        downloadRequest.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonPayload));
        downloadRequest.downloadHandler = new DownloadHandlerBuffer();
        downloadRequest.SetRequestHeader("Authorization", "Bearer " + hyper3dApiKey);
        downloadRequest.SetRequestHeader("Content-Type", "application/json");

        await downloadRequest.SendWebRequestAsync();

        if (downloadRequest.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"Download request failed: {downloadRequest.error}. Response: {downloadRequest.downloadHandler.text}");
            outputText.text += "\nFailed to fetch download links.";
            return false;
        }

        string responseText = downloadRequest.downloadHandler.text;
        Debug.Log("Download Response: " + responseText);
        
        var downloadResp = JsonUtility.FromJson<Hyper3DDownloadResponse>(responseText);
        
        // Check for an API error in the response body (e.g., {"error": "..."})
        if (!string.IsNullOrEmpty(downloadResp.error))
        {
            Debug.LogError($"Download API Error: {downloadResp.error}");
            outputText.text += $"\nDownload error: {downloadResp.error}";
            return false;
        }
        
        // Success check: Ensure the list exists and has at least two entries (Model + Preview)
        if (downloadResp.list != null && downloadResp.list.Length >= 2)
        {
            // Assuming the first entry is the GLB model and the second is the preview image
            string glbUrl = downloadResp.list[0].url;
            string previewUrl = downloadResp.list[1].url;
            
            outputText.text += "\nDownload links retrieved! Starting asset load...";

            // Call your existing method to fetch and load the files
            await DownloadAndLoadModelAsync(glbUrl, previewUrl);
            return true;
        }
        else
        {
            Debug.LogError("Download response did not contain the expected asset URLs (Model and Preview).");
            outputText.text += "\nError: Download links were missing from the response.";
            return false;
        }
    }
}

// -------------------
// Load GLB (using GLTFast)
// -------------------
private async Task LoadModelFromURLAsync(string url)
{
    outputText.text += "\nLoading model...";

    var gltfImport = new GLTFast.GltfImport();
    
    bool success = await gltfImport.Load(url);

    if (success)
    {
        if (currentModel != null) Destroy(currentModel);
        currentModel = new GameObject("GeneratedModel");
        await gltfImport.InstantiateMainSceneAsync(currentModel.transform);
        currentModel.transform.position = Vector3.zero;
        outputText.text += "\nModel loaded!";
    }
    else
    {
        outputText.text += "\nFailed to load GLB model using GLTFast.";
    }
}

// Define this class specifically for the download request payload
[System.Serializable]
public class Hyper3DDownloadRequest
{
    public string task_uuid; // The key required by the /api/v2/download endpoint
}

[System.Serializable]
public class DiffusionRequest
{
    public string prompt;
}

    // Add these (or similar) classes at the bottom of your script
[System.Serializable]
private class Hyper3DFileEntry
{
    public string url;
    public string name;
    // Hyper3D responses often have other fields like type or size, 
    // but url and name are typically enough for parsing
}

    // [System.Serializable]
    // private class Hyper3DStatusResponse
    // {
    //     // This is the property your code is trying to access!
    //     public Hyper3DFileEntry[] list; 
    // }


// Define this class outside of any method
[System.Serializable]
public class Hyper3DPollRequest
{
    public string subscription_key;
}

[System.Serializable]
private class Hyper3DJobEntry
{
    // The status field is the most important for polling
    public string status; 
    public string uuid;
}

[System.Serializable]
private class Hyper3DStatusResponse
{
    // The main array to check
    public Hyper3DJobEntry[] jobs; 
    
    // For handling immediate errors
    public string error; 
}

[System.Serializable]
private class Hyper3DJobDetails
{
    // The key we need to extract for polling
    public string subscription_key; 
    public string[] uuids; // Array of individual job IDs
}

[System.Serializable]
private class Hyper3DUploadResponse
{
    public string message;
    public string prompt;
    public string submit_time;
    
    // The main task UUID (which your code currently uses)
    public string uuid; 

    // The nested object holding the subscription key
    public Hyper3DJobDetails jobs; 
}

[System.Serializable]
private class Hyper3DDownloadFile
{
    // Confirmed from the documentation
    public string url; 
    public string name;
}

[System.Serializable]
private class Hyper3DDownloadResponse
{
    // Confirmed: The array holding the files is named 'list'
    public Hyper3DDownloadFile[] list; 
    
    // Confirmed: For handling errors
    public string error;
}



// NOTE: Ensure your existing Hyper3DPollRequest class is still defined as:
/*
[System.Serializable]
public class Hyper3DPollRequest
{
    public string subscription_key;
}
*/

private string ExtractTaskUUIDJSON(string json)
{
    try
    {
        // Deserializing the entire JSON object
        Hyper3DUploadResponse resp = JsonUtility.FromJson<Hyper3DUploadResponse>(json);
        return resp.uuid ?? "unknown";
    }
    catch
    {
        return "unknown";
    }
}

// Add this new method to extract the nested subscription key
private string ExtractSubscriptionKey(string json)
{
    try
    {
        Hyper3DUploadResponse resp = JsonUtility.FromJson<Hyper3DUploadResponse>(json);
        
        // Check if the nested 'jobs' object and the 'subscription_key' property are present
        if (resp.jobs != null && !string.IsNullOrEmpty(resp.jobs.subscription_key))
        {
            return resp.jobs.subscription_key;
        }
        return "unknown";
    }
    catch
    {
        return "unknown";
    }
}
// Define this class outside of any method
// [System.Serializable]
// public class Hyper3DPollRequest
// {
//     // FIX: Must match the parameter name used in the JSON payload creation.
//     public string subscription_key; 
// }
    // private string ExtractModelUrlJSON(string json)
    // {
    //     try
    //     {
    //         Hyper3DDownloadResponse resp = JsonUtility.FromJson<Hyper3DDownloadResponse>(json);
    //         return resp.model?.url ?? "unknown";
    //     }
    //     catch { return "unknown"; }
    // }

private string ExtractContent(string json)
{
    try
    {
        var sb = new StringBuilder();
        var lines = json.Split(new[] { '\n' }, System.StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            // Parse each line safely
            var match = Regex.Match(line, "\"content\"\\s*:\\s*\"(.*?)\"", RegexOptions.Compiled);
            if (match.Success)
            {
                string chunk = Regex.Unescape(match.Groups[1].Value);
                sb.Append(chunk);
            }
        }

        string fullContent = sb.ToString().Trim();

        if (string.IsNullOrEmpty(fullContent))
            return "No content";

        return fullContent;
    }
    catch
    {
        return "No content";
    }
}


    private string ExtractModelUrl(string json)
    {
        int idx = json.IndexOf("url");
        if (idx == -1) return "unknown";
        int start = json.IndexOf("\"", idx + 5) + 1;
        int end = json.IndexOf("\"", start);
        return json.Substring(start, end - start);
    }
}

// --- Extension method to await UnityWebRequest ---
public static class UnityWebRequestExtensions
{
    public static Task SendWebRequestAsync(this UnityWebRequest request)
    {
        var tcs = new TaskCompletionSource<bool>();
        var operation = request.SendWebRequest();
        operation.completed += _ => tcs.SetResult(true);
        return tcs.Task;
    }
}

