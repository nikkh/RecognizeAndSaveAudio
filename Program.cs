using System;
using System.IO;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using System.Linq;
using Microsoft.Extensions.Configuration;
using System.Text;
using System.Dynamic;

namespace mspeech
{
    class ToneGenerator
    {
        public ToneGenerator(int frequency)
        {
            m_provider = new SignalGenerator();
            m_provider.Frequency = frequency;
            m_provider.Gain = .1;

            m_speakers = new WaveOut();
            m_speakers.Init(m_provider);
        }

        public void Play(int milliseconds)
        {
            Task.Run(() => {
                m_speakers.Play();
                Thread.Sleep(milliseconds);
                m_speakers.Stop();
            });
        }

        WaveOut m_speakers;
        SignalGenerator m_provider;
    }
    class MicrosoftSpeechRecognizer
    {
        dynamic parameters;

        public MicrosoftSpeechRecognizer(ExpandoObject d)
        {
           
            m_tonegen = new ToneGenerator(660);
            parameters = d;
        }

        public List<string> RecognizeSpeech(string input = "microphone")
        {
            var task = RecognizeSpeechAsync(input);
            task.Wait();

            return task.Result;
        }

        public async Task<List<string>> RecognizeSpeechAsync(string input)
        {
            Console.WriteLine("RecognizeSpeechAsync");
            var audioStream = AudioInputStream.CreatePushStream(AudioStreamFormat.GetWaveFormatPCM((uint)sampleRate, 16, 1));
            var audioConfig = AudioConfig.FromStreamInput(audioStream);

            var speechConfig = SpeechConfig.FromSubscription(parameters.Key, parameters.Region);
            speechConfig.SpeechRecognitionLanguage = languageCode;
            //speechConfig.SetProperty(PropertyId.SpeechServiceConnection_ProxyHostName, "localhost");
            //speechConfig.SetProperty(PropertyId.SpeechServiceConnection_ProxyPort, "8888");

            var recognizer = new SpeechRecognizer(speechConfig, audioConfig);

            
            var results = ProcessResultsAsync(recognizer);
            var audio = PumpAudio(audioStream, input);
            Task.WaitAll(audio, results);

            return results.Result;
        }

        private async Task<List<string>> ProcessResultsAsync(SpeechRecognizer recognizer)
        {
            Console.WriteLine("ProcessResultsAsync");
            List<string> results = new List<string>();

            recognizer.Recognizing += (sender, e) => { Console.WriteLine("INTERMEDIATE: {0} ... ", e.Result.Text); };
            recognizer.Recognized += (sender, e) => {
                if (!string.IsNullOrEmpty(e.Result.Text))
                {
                    Console.WriteLine("FINAL RESULT: '{0}'", e.Result.Text);
                    m_tonegen.Play(50);
                    results.Add(e.Result.Text);
                }
            };

            var done = new ManualResetEvent(false);
            recognizer.SessionStopped += (sender, e) => done.Set();
            recognizer.Canceled += (sender, e) => done.Set();

            await recognizer.StartContinuousRecognitionAsync();
            done.WaitOne();

            return results;
        }

        private async Task PumpAudio(PushAudioInputStream audioStream, string input)
        {
            Console.WriteLine("PumpAudio");
            Func<byte[], int, Task> processor = (byte[] audio, int size) => {
                return Task.Run(() => audioStream.Write(audio, size));
            };

            SyncStart();

            if (!string.IsNullOrEmpty(input) && input != "microphone")
            {
                await PumpAudioFromFile(processor, input);
            }
            else
            {
                PumpAudioFromMicrophone(processor);
            }

            audioStream.Close();
        }

        private void SyncStart()
        {
            Console.WriteLine("SyncStart");
            var seconds = (int)DateTime.Now.Subtract(DateTime.Today).TotalSeconds;
            seconds = seconds - (seconds % 2) + 2;
            while (DateTime.Now.Subtract(DateTime.Today).TotalSeconds < seconds) ;
        }

        private async Task PumpAudioFromFile(Func<byte[], int, Task> audioProcessor, string fileName)
        {
            Console.WriteLine("PumpAudioFromFile");
            using (FileStream fileStream = new FileStream(fileName, FileMode.Open))
            {
                int bytesRead;
                var buffer = new byte[32 * 1024];

                while ((bytesRead = await fileStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await audioProcessor(buffer, bytesRead);
                };
            }
        }

        private void PumpAudioFromMicrophone(Func<byte[], int, Task> audioProcessor)
        {
            WaveFileWriter waveFile=null;
            Console.WriteLine("PumpAudioFromMicrophone");
            using (var wavein = new WaveInEvent())
            {
                
                wavein.DataAvailable += async (sender, e) =>
                {
                    await audioProcessor(e.Buffer, e.BytesRecorded);
                    if (waveFile != null)
                    {
                        await waveFile.WriteAsync(e.Buffer, 0, e.BytesRecorded);
                        await waveFile.FlushAsync();
                    }
                   
                };

                wavein.WaveFormat = new WaveFormat(sampleRate, 16, 1);
                wavein.NumberOfBuffers = 20;
                string path = $"{parameters.OutputFilePath}SR-{parameters.StartTime}.{parameters.SoundFileExt}";
                waveFile = new WaveFileWriter(path, wavein.WaveFormat);
                wavein.StartRecording();

                Console.WriteLine("Listening... (press ENTER to exit) {0}", DateTime.Now.Subtract(DateTime.Today).TotalMilliseconds);
                Console.ReadLine();
               
                wavein.StopRecording();
               
            }
        }

        private readonly int sampleRate = 16000;
        private readonly string languageCode = "en-US";
        private ToneGenerator m_tonegen;
    }

    class Program
    {
        private static string key;
        private static string region;
        private static string outputFilePath;

        private const string soundFileExt = "wav";
        private const string textFileExt = "txt";

        private static long startTime = DateTime.Now.Ticks;

        static void Main(string[] args)
        {
            if (args.Length > 1)
            {
                Console.WriteLine("mspeech input\n\n\tinput can be a file\n\tinput can be microphone");
                return;
            }

            var configBuilder = new ConfigurationBuilder()
              .SetBasePath(Directory.GetCurrentDirectory())
              .AddJsonFile("local.settings.json", optional: true, reloadOnChange: true)
              .AddJsonFile("settings.json", optional: true, reloadOnChange: true)
              .AddEnvironmentVariables();

            IConfiguration configuration = configBuilder.Build();
            key = configuration["SpeechRecognizerKey"];
            region = configuration["SpeechRecognizerRegion"];
            outputFilePath = configuration["OutputFilePath"];


            dynamic d = new ExpandoObject();
            d.Key = key;
            d.Region = region;
            d.OutputFilePath = outputFilePath;
            d.StartTime = startTime;
            d.SoundFileExt = soundFileExt;

            MicrosoftSpeechRecognizer recognizer = new MicrosoftSpeechRecognizer(d);
            var results = recognizer.RecognizeSpeech(args.Length == 1 ? args[0] : "microphone");
            string path = $"{outputFilePath}SR-{startTime}.{textFileExt}";
            File.WriteAllLines(path, results, Encoding.UTF8);
        }
    }
}
