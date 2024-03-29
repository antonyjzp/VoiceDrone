﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.ComponentModel;
using Microsoft.Speech.Recognition;
using System.IO;
using Microsoft.Speech.AudioFormat;
using Microsoft.Kinect;
using System.Net.Sockets;

namespace VoiceDrone
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        /* private */
        KinectSensor _kinectSensor;
        SpeechRecognitionEngine _sre;
        KinectAudioSource _source;

        public MainWindow()
        {
            InitializeComponent();
            this.DataContext = this;
            
            /* unloaded */
            this.Unloaded += delegate
            {
                _kinectSensor.SkeletonStream.Disable();
                _sre.RecognizeAsyncCancel();
                _sre.RecognizeAsyncStop();
                _sre.Dispose();
            };

            /* loaded */
            this.Loaded += delegate
            {
                _kinectSensor = KinectSensor.KinectSensors[0];
                /* make moving smooth */
                _kinectSensor.SkeletonStream.Enable(new TransformSmoothParameters()
                {
                    Correction = 0.5f,
                    JitterRadius = 0.05f,
                    MaxDeviationRadius = 0.04f,
                    Smoothing = 0.5f
                });
                _kinectSensor.SkeletonFrameReady += nui_SkeletonFrameReady;
                _kinectSensor.Start();
                /* speech recognition */
                StartSpeechRecognition();   
            };
        }

        /* 
         * get right hand skeleton information 
         */
        void nui_SkeletonFrameReady(object sender, SkeletonFrameReadyEventArgs e)
        {
            using (SkeletonFrame skeletonFrame = e.OpenSkeletonFrame())
            {
                if (skeletonFrame == null)
                    return;

                var skeletons = new Skeleton[skeletonFrame.SkeletonArrayLength];
                skeletonFrame.CopySkeletonDataTo(skeletons);
                /* search right hand */
                foreach (Skeleton skeletonData in skeletons)
                {
                    if (skeletonData.TrackingState == SkeletonTrackingState.Tracked)
                    {
                        Microsoft.Kinect.SkeletonPoint rightHandVec = skeletonData.Joints[JointType.HandRight].Position;
                        var depthPoint = _kinectSensor.MapSkeletonPointToDepth(rightHandVec, DepthImageFormat.Resolution640x480Fps30);
                        /* set hand position in screen */
                        HandTop = depthPoint.Y * this.MainStage.ActualHeight / 480;
                        HandLeft = depthPoint.X * this.MainStage.ActualWidth / 640;
                        
                    }
                }
            }
        }

        /*
         * speech recognition
         */
        private void StartSpeechRecognition()
        {
            /* get audio source */
            _source = CreateAudioSource();

            /* matching function */
            Func<RecognizerInfo, bool> matchingFunc = r =>
            {
                string value;
                r.AdditionalInfo.TryGetValue("Kinect", out value);
                return "True".Equals(value, StringComparison.InvariantCultureIgnoreCase)
                    && "en-US".Equals(r.Culture.Name, StringComparison.InvariantCultureIgnoreCase);
            };
            RecognizerInfo ri = SpeechRecognitionEngine.InstalledRecognizers().Where(matchingFunc).FirstOrDefault();

            /* initialiser the engine */
            _sre = new SpeechRecognitionEngine(ri.Id);
            
            /* create grammars to be recognized */
            CreateGrammars(ri);
            
            /* add 3 functions: recognized, hypothesized, recognition rejected */
            _sre.SpeechRecognized += sre_SpeechRecognized;
            _sre.SpeechHypothesized += sre_SpeechHypothesized;
            _sre.SpeechRecognitionRejected += sre_SpeechRecognitionRejected;

            Stream s = _source.Start();
            _sre.SetInputToAudioStream(s,
                                       new SpeechAudioFormatInfo(
                                       EncodingFormat.Pcm, 16000, 16, 1,
                                       32000, 2, null));
            _sre.RecognizeAsync(RecognizeMode.Multiple);
        }

        void sre_SpeechRecognitionRejected(object sender, SpeechRecognitionRejectedEventArgs e)
        {
            HypothesizedText += " Rejected";
            Confidence = Math.Round(e.Result.Confidence, 2).ToString();
        }

        void sre_SpeechHypothesized(object sender, SpeechHypothesizedEventArgs e)
        {
            HypothesizedText = e.Result.Text;
        }

        void sre_SpeechRecognized(object sender, SpeechRecognizedEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action<SpeechRecognizedEventArgs>(InterpretCommand), e);
        }

        /*
         * create voice grammar
         */
        private void CreateGrammars(RecognizerInfo ri)
        {
            var create = new Choices();
            create.Add("move");
            create.Add("go");
            
            var direction = new Choices();
            direction.Add("front");
            direction.Add("left");
            direction.Add("right");
            direction.Add("back");
            direction.Add("up");
            direction.Add("down");

            //var shapes = new Choices();
            //shapes.Add("circle");
            //shapes.Add("triangle");
            //shapes.Add("square");
            //shapes.Add("diamond");

            var gb = new GrammarBuilder();
            gb.Culture = ri.Culture;
            gb.Append(create);
            gb.AppendWildcard();
            gb.Append(direction);
            //gb.Append(shapes);
            gb.Append("roger that");

            var g = new Grammar(gb);
            _sre.LoadGrammar(g);

            var q = new GrammarBuilder { Culture = ri.Culture };
            q.Append("quit application");
            var quit = new Grammar(q);

            _sre.LoadGrammar(quit);
        }

        private void InterpretCommand(SpeechRecognizedEventArgs e)
        {
            var result = e.Result;
            Confidence = Math.Round(result.Confidence, 2).ToString();
            if (result.Confidence < 95 && result.Words[0].Text == "quit" && result.Words[1].Text == "application")
            {
                this.Close();
            }
            if (result.Words[0].Text == "move" || result.Words[0].Text == "go")
            {
                var colorString = result.Words[2].Text;
                Color color;
                switch (colorString)
                {
                    case "front": color = Colors.Cyan;
                        break;
                    case "left": color = Colors.Yellow;
                        break;
                    case "right": color = Colors.Magenta;
                        break;
                    case "back": color = Colors.Blue;
                        break;
                    case "up": color = Colors.Green;
                        break;
                    case "down": color = Colors.Red;
                        break;
                    default:
                        return;
                }

                //var shapeString = result.Words[3].Text;
                Shape shape = new Ellipse();
                shape.Width = 150;
                shape.Height = 150;
                //switch (shapeString)
                //{
                //    case "circle":
                //        shape = new Ellipse();
                //        shape.Width = 150;
                //        shape.Height = 150;
                //        break;
                //    case "square":
                //        shape = new Rectangle();
                //        shape.Width = 150;
                //        shape.Height = 150;
                //        break;
                //    case "triangle":
                //        var poly = new Polygon();
                //        poly.Points.Add(new Point(0, 0));
                //        poly.Points.Add(new Point(150, 0));
                //        poly.Points.Add(new Point(75, -150));
                //        shape = poly;
                //        break;
                //    case "diamond":
                //        var poly2 = new Polygon();
                //        poly2.Points.Add(new Point(0, 0));
                //        poly2.Points.Add(new Point(75, 150));
                //        poly2.Points.Add(new Point(150, 0));
                //        poly2.Points.Add(new Point(75, -150));
                //        shape = poly2;
                //        break;
                //    default:
                //        return;
                //}
                /* set shape position and color */
                shape.SetValue(Canvas.LeftProperty, HandLeft);
                shape.SetValue(Canvas.TopProperty, HandTop);
                shape.Fill = new SolidColorBrush(color);
                MainStage.Children.Add(shape);

                //try
                //{
                //    TcpClient tc = new TcpClient("server", 1800);
                //    Console.WriteLine(result.Words[0].Text + result.Words[1].Text);
                //    NetworkStream ns = tc.GetStream();
                //    StreamWriter sw = new StreamWriter(ns);
                //    sw.WriteLine(result.Words[0].Text + result.Words[1].Text);
                //    sw.Flush();
                //    StreamReader sr = new StreamReader(ns);
                //    Console.WriteLine(sr.ReadLine());
                //}
                //catch (Exception e2) 
                //{ 
                //    Console.WriteLine(e2); 
                //}

            }
        }

        /*
         * binding UI
         */
        private double _handLeft;
        public double HandLeft
        {
            get { return _handLeft; }
            set
            {
                _handLeft = value;
                OnPropertyChanged("HandLeft");
            }

        }

        private double _handTop;
        public double HandTop
        {
            get { return _handTop; }
            set
            {
                _handTop = value;
                OnPropertyChanged("HandTop");
            }
        }

        private string _hypothesizedText;
        public string HypothesizedText
        {
            get { return _hypothesizedText; }
            set
            {
                _hypothesizedText = value;
                OnPropertyChanged("HypothesizedText");
            }
        }

        private string _confidence;
        public string Confidence
        {
            get { return _confidence; }
            set
            {
                _confidence = value;
                OnPropertyChanged("Confidence");
            }
        }


        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged(string propertyName)
        {
            if (PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
            }

        }

        /*
         * get kinect audio source
         */
        private KinectAudioSource CreateAudioSource()
        {
            var source = KinectSensor.KinectSensors[0].AudioSource;
            source.AutomaticGainControlEnabled = false;
            source.EchoCancellationMode = EchoCancellationMode.None;
            return source;
        }
    }

}
