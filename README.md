# JakeyTTS

## ⚠️ Important Setup Instructions

To run JakeyTTS, you **must** download the Kokoro TTS model separately because it is too large to be hosted on GitHub. 

1. Download `kokoro-v1.0.onnx`.
2. Place the downloaded file inside the `Assets/` directory of this project so the application can load it successfully.

Failure to include this asset will result in the application crashing on launch.

## 📄 Third-Party Licenses

**Kokoro TTS Model**  
The underlying Kokoro TTS acoustic model (`kokoro-v1.0.onnx`) is licensed under the **Apache License 2.0**. 
By downloading and using the model with JakeyTTS, you agree to comply with the terms of the Apache 2.0 license. You can use it freely for both personal and commercial purposes.