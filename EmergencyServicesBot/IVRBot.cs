namespace EmergencyServicesBot
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Threading.Tasks;
    using Microsoft.Bot.Builder.Calling;
    using Microsoft.Bot.Builder.Calling.Events;
    using Microsoft.Bot.Builder.Calling.ObjectModel.Contracts;
    using Microsoft.Bot.Builder.Calling.ObjectModel.Misc;
    using Microsoft.Bot.Connector;

    public class IVRBot : IDisposable, ICallingBot
    {
        // DTMF keys required for each of option, will be used for parsing results of recognize
        private const string Support = "2";

        // Response messages depending on user selection
        private const string WelcomeMessage = "Hello, you have successfully contacted the Emergency Services Bot.";
        private const string MainMenuPromptMessage = "If you have a life threatening medical emergency please contact the emergency services or go to your nearest hospital.  For non-life threatening situations please press 2.";
        private const string NoConsultantsMessage = "Whilst we wait to connect you, please leave your name and a description of your problem. You can press the hash key when finished. We will call you as soon as possible.";
        private const string EndingMessage = "Thank you for leaving the message, goodbye";

        private readonly Dictionary<string, CallState> callStateMap = new Dictionary<string, CallState>();

        private readonly MicrosoftCognitiveSpeechService speechService = new MicrosoftCognitiveSpeechService();

        private IEnumerable<Participant> participants;

        public IVRBot(ICallingBotService callingBotService)
        {
            if (callingBotService == null)
            {
                throw new ArgumentNullException(nameof(callingBotService));
            }

            this.CallingBotService = callingBotService;

            this.CallingBotService.OnIncomingCallReceived += this.OnIncomingCallReceived;
            this.CallingBotService.OnPlayPromptCompleted += this.OnPlayPromptCompleted;
            this.CallingBotService.OnRecordCompleted += this.OnRecordCompleted;
            this.CallingBotService.OnRecognizeCompleted += this.OnRecognizeCompleted;
            this.CallingBotService.OnHangupCompleted += OnHangupCompleted;
        }

        public ICallingBotService CallingBotService { get; }

        public void Dispose()
        {
            if (this.CallingBotService != null)
            {
                this.CallingBotService.OnIncomingCallReceived -= this.OnIncomingCallReceived;
                this.CallingBotService.OnPlayPromptCompleted -= this.OnPlayPromptCompleted;
                this.CallingBotService.OnRecordCompleted -= this.OnRecordCompleted;
                this.CallingBotService.OnRecognizeCompleted -= this.OnRecognizeCompleted;
                this.CallingBotService.OnHangupCompleted -= OnHangupCompleted;
            }
        }

        private static Task OnHangupCompleted(HangupOutcomeEvent hangupOutcomeEvent)
        {
            hangupOutcomeEvent.ResultingWorkflow = null;
            return Task.FromResult(true);
        }

        private static void SetupInitialMenu(Workflow workflow)
        {
            workflow.Actions = new List<ActionBase> { CreateIvrOptions(MainMenuPromptMessage, 5, false) };
        }

        private static void ProcessMainMenuSelection(RecognizeOutcomeEvent outcome, CallState callStateForClient)
        {
            if (outcome.RecognizeOutcome.Outcome != Outcome.Success)
            {
                SetupInitialMenu(outcome.ResultingWorkflow);
                return;
            }

            switch (outcome.RecognizeOutcome.ChoiceOutcome.ChoiceName)
            {
                case Support:
                    callStateForClient.ChosenMenuOption = Support;
                    SetupRecording(outcome.ResultingWorkflow);
                    break;
                default:
                    SetupInitialMenu(outcome.ResultingWorkflow);
                    break;
            }
        }

        private static void ProcessEmergency(RecognizeOutcomeEvent outcome)
        {
            SetupRecording(outcome.ResultingWorkflow);
        }

        private static Recognize CreateIvrOptions(string textToBeRead, int numberOfOptions, bool includeBack)
        {
            if (numberOfOptions > 9)
            {
                throw new Exception("too many options specified");
            }

            var choices = new List<RecognitionOption>();

            for (int i = 1; i <= numberOfOptions; i++)
            {
                choices.Add(new RecognitionOption { Name = Convert.ToString(i), DtmfVariation = (char)('0' + i) });
            }

            if (includeBack)
            {
                choices.Add(new RecognitionOption { Name = "#", DtmfVariation = '#' });
            }

            var recognize = new Recognize
            {
                OperationId = Guid.NewGuid().ToString(),
                PlayPrompt = GetPromptForText(textToBeRead),
                BargeInAllowed = true,
                Choices = choices
            };

            return recognize;
        }

        private static void SetupRecording(Workflow workflow)
        {
            var id = Guid.NewGuid().ToString();

            var prompt = GetPromptForText(NoConsultantsMessage);
            var record = new Record
            {
                OperationId = id,
                PlayPrompt = prompt,
                MaxDurationInSeconds = 60,
                InitialSilenceTimeoutInSeconds = 5,
                MaxSilenceTimeoutInSeconds = 4,
                PlayBeep = true,
                RecordingFormat = RecordingFormat.Wav,
                StopTones = new List<char> { '#' }
            };
            workflow.Actions = new List<ActionBase> { record };
        }

        private static PlayPrompt GetPromptForText(string text)
        {
            var prompt = new Prompt { Value = text, Voice = VoiceGender.Male };
            return new PlayPrompt { OperationId = Guid.NewGuid().ToString(), Prompts = new List<Prompt> { prompt } };
        }

        private Task OnIncomingCallReceived(IncomingCallEvent incomingCallEvent)
        {
            this.callStateMap[incomingCallEvent.IncomingCall.Id] = new CallState();

            incomingCallEvent.ResultingWorkflow.Actions = new List<ActionBase>
                {
                    new Answer { OperationId = Guid.NewGuid().ToString() },
                    GetPromptForText(WelcomeMessage)
                };

            // Save the participants so that we can use them later for pro-active message
            // This would need to be stored/keyed for multiple users!
            this.participants = incomingCallEvent.IncomingCall.Participants;
            return Task.FromResult(true);
        }

        private Task OnPlayPromptCompleted(PlayPromptOutcomeEvent playPromptOutcomeEvent)
        {
            var callStateForClient = this.callStateMap[playPromptOutcomeEvent.ConversationResult.Id];
            SetupInitialMenu(playPromptOutcomeEvent.ResultingWorkflow);

            return Task.FromResult(true);
        }

        private async Task OnRecordCompleted(RecordOutcomeEvent recordOutcomeEvent)
        {
            recordOutcomeEvent.ResultingWorkflow.Actions = new List<ActionBase>
                {
                    GetPromptForText(EndingMessage),
                    new Hangup { OperationId = Guid.NewGuid().ToString() }
                };

            // Convert the audio to text
            if (recordOutcomeEvent.RecordOutcome.Outcome == Outcome.Success)
            {
                var record = await recordOutcomeEvent.RecordedContent;
                string text = await this.GetTextFromAudioAsync(record);
                await this.SendSTTResultToUser("We detected the following audio: " + text);
            }

            recordOutcomeEvent.ResultingWorkflow.Links = null;
            this.callStateMap.Remove(recordOutcomeEvent.ConversationResult.Id);
        }

        private async Task SendSTTResultToUser(string text)
        {
            foreach (var participant in this.participants)
            {
                if (participant.Originator)
                {
                    AgentListener.ToId = participant.Identity;
                    AgentListener.ToName = participant.DisplayName;
                    AgentListener.ConversationId = participant.Identity; // same as channelid
                }
                else
                {
                    AgentListener.FromId = participant.Identity;
                    AgentListener.FromName = participant.DisplayName;
                }
            }

            AgentListener.ChannelId = "skype";
            AgentListener.ServiceUrl = "https://skype.botframework.com";
            MicrosoftAppCredentials.TrustServiceUrl(AgentListener.ServiceUrl);
            await AgentListener.Resume(text);
        }

        /// <summary>
        /// Gets text from an audio stream.
        /// </summary>
        /// <param name="audiostream"></param>
        /// <returns>Transcribed text. </returns>
        private async Task<string> GetTextFromAudioAsync(Stream audiostream)
        {
            var text = await this.speechService.GetTextFromAudioAsync(audiostream);
            Debug.WriteLine(text);
            return text;
        }

        private Task OnRecognizeCompleted(RecognizeOutcomeEvent recognizeOutcomeEvent)
        {
            var callStateForClient = this.callStateMap[recognizeOutcomeEvent.ConversationResult.Id];

            ProcessMainMenuSelection(recognizeOutcomeEvent, callStateForClient);

            return Task.FromResult(true);
        }

        private class CallState
        {
            public string ChosenMenuOption { get; set; }
        }
    }
}