# JakeyTTS

## ⚠️ Important Setup Instructions

To run JakeyTTS, you **must** download the Kokoro TTS model separately because it is too large to be hosted on GitHub. 

1. Download `kokoro-v1.0.onnx`.
2. Place the downloaded file inside the `Assets/` directory of this project so the application can load it successfully.

Failure to include this asset will result in the application crashing on launch.