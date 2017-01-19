namespace EmergencyServicesBot
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Threading.Tasks;
    using System.Web.Configuration;
    using Microsoft.Bing.Speech;
    using Newtonsoft.Json;

    public class MicrosoftCognitiveSpeechService
    {
        private readonly string subscriptionKey;
        private readonly string speechRecognitionUri;

        public MicrosoftCognitiveSpeechService()
        {
            this.DefaultLocale = "en-US";
            this.subscriptionKey = WebConfigurationManager.AppSettings["MicrosoftSpeechApiKey"];
            this.speechRecognitionUri = Uri.UnescapeDataString(WebConfigurationManager.AppSettings["MicrosoftSpeechRecognitionUri"]);
        }

        public string DefaultLocale { get; set; }

        // public async Task GetText1(Stream audiostream)
        // {
        //    var preferences = new Preferences(this.DefaultLocale, new Uri(this.speechRecognitionUri), new CognitiveServicesAuthorizationProvider(this.subscriptionKey));
        //    // Create a a speech client
        //    using (var speechClient = new SpeechClient(preferences))
        //    {
        //        speechClient.SubscribeToPartialResult(this.OnPartialResultAsync);
        //        speechClient.SubscribeToRecognitionResult(this.OnRecognitionResult);
        //        // create an audio content and pass it a stream.
        //        var deviceMetadata = new DeviceMetadata(DeviceType.Near, DeviceFamily.Desktop, NetworkType.Wifi, OsName.Windows, "1607", "Dell", "T3600");
        //        var applicationMetadata = new ApplicationMetadata("SampleApp", "1.0.0");
        //        var requestMetadata = new RequestMetadata(Guid.NewGuid(), deviceMetadata, applicationMetadata, "SampleAppService");
        //        await speechClient.RecognizeAsync(new SpeechInput(audiostream, requestMetadata), CancellationToken.None).ConfigureAwait(false);
        //    }
        // }

        /// <summary>
        /// Invoked when the speech client receives a partial recognition hypothesis from the server.
        /// </summary>
        /// <param name="args">The partial response recognition result.</param>
        /// <returns>
        /// A task
        /// </returns>
        // public Task OnPartialResultAsync(RecognitionPartialResult args)
        // {
        //    Debug.WriteLine("--- Partial result received by OnPartialResult ---");
        //    Debug.WriteLine(args.DisplayText);
        //    return AgentListener.Resume(args.DisplayText);
        //    // return CompletedTask;
        // }

        /// <summary>
        /// Invoked when the speech client receives a phrase recognition result(s) from the server.
        /// </summary>
        /// <param name="args">The recognition result.</param>
        /// <returns>
        /// A task
        /// </returns>
        // public Task OnRecognitionResult(RecognitionResult args)
        // {
        //    var response = args;
        //    Debug.WriteLine("--- Phrase result received by OnRecognitionResult ---");
        //    Debug.WriteLine("***** Phrase Recognition Status = [{0}] ***", response.RecognitionStatus);
        //    if (response.Phrases != null)
        //    {
        //        foreach (var result in response.Phrases)
        //        {
        //            Debug.WriteLine("{0} (Confidence:{1})", result.DisplayText, result.Confidence);
        //        }
        //    }
        //    return Task.FromResult(true);
        // }

        /// <summary>
        /// Gets text from an audio stream.
        /// </summary>
        /// <param name="audiostream"></param>
        /// <returns>Transcribed text. </returns>
        public async Task<string> GetTextFromAudioAsync(Stream audiostream)
        {
            var requestUri = this.speechRecognitionUri + Guid.NewGuid();

            using (var client = new HttpClient())
            {
                var token = Authentication.Instance.GetAccessToken();
                client.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);

                try
                {
                    using (var binaryContent = new ByteArrayContent(StreamToBytes(audiostream)))
                    {
                        // binaryContent.Headers.TryAddWithoutValidation("content-type", "audio/wav; codec=\"audio/pcm\"; samplerate=16000");
                        var response = await client.PostAsync(requestUri, binaryContent);
                        var responseString = await response.Content.ReadAsStringAsync();
                        dynamic data = JsonConvert.DeserializeObject(responseString);

                        if (data != null)
                        {
                            return data.header.name;
                        }
                        else
                        {
                            return string.Empty;
                        }
                    }
                }
                catch (Exception exp)
                {
                    Debug.WriteLine(exp);
                    return string.Empty;
                }
            }
        }

        /// <summary>
        /// Converts Stream into byte[].
        /// </summary>
        /// <param name="input">Input stream</param>
        /// <returns>Output byte[]</returns>
        private static byte[] StreamToBytes(Stream input)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                input.CopyTo(ms);
                return ms.ToArray();
            }
        }
    }
}