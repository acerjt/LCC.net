﻿using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LandscapeClassifier.Annotations;
using MathNet.Numerics.LinearAlgebra;

namespace LandscapeClassifier.ViewModel.MainWindow.Classification
{
    public class BandViewModel : INotifyPropertyChanged
    {
        private WriteableBitmap _bandImage;

        private int _bitmapImagePixelWidth;
        private int _bitmapImagePixelHeight;
        private bool _isVisible;
        private bool _isFeature = true;
        private bool _canChangeIsFeature = true;

        private Brush _currentPositionBrush;
        private bool _isRgb;

        public readonly Matrix<double> Transform;

        /// <summary>
        /// Index of the band.
        /// </summary>
        public int BandNumber { get; }

        /// <summary>
        /// Meters per pixel of the band.
        /// </summary>
        public double MetersPerPixel { get; }

        /// <summary>
        /// Current Color of the mouse position.
        /// </summary>
        public Brush CurrentPositionBrush
        {
            get { return _currentPositionBrush;  }
            set { _currentPositionBrush = value; OnPropertyChanged(nameof(CurrentPositionBrush)); }
        }

        /// <summary>
        /// Path to the file of this band.
        /// </summary>
        public string BandPath { get; set; }

        /// <summary>
        /// Title of the tab.
        /// </summary>
        public string BandName { get; set; }

        /// <summary>
        /// Band upper left in world coordinates.
        /// </summary>
        public readonly Vector<double> UpperLeft;

        /// <summary>
        /// Band bottom right in world coordinates.
        /// </summary>
        public readonly Vector<double> BottomRight;

        /// <summary>
        /// Pixel width of the image.
        /// </summary>
        public double ImagePixelWidth => _bitmapImagePixelWidth;

        /// <summary>
        /// Pixel height of the image.
        /// </summary>
        public double ImagePixelHeight => _bitmapImagePixelHeight;

        /// <summary>
        /// Max cut for scale.
        /// </summary>
        public int MaxCutScale { get; }

        /// <summary>
        /// Min cut for scale.
        /// </summary>
        public int MinCutScale { get; }

        /// <summary>
        /// The band image.
        /// </summary>
        public WriteableBitmap BandImage
        {
            get { return _bandImage; }
            set
            {
                if (value != _bandImage)
                {
                    _bandImage = value;
                    _bitmapImagePixelWidth = _bandImage.PixelWidth;
                    _bitmapImagePixelHeight = _bandImage.PixelHeight;

                    OnPropertyChanged(nameof(BandImage));
                }
            }
        }

        /// <summary>
        /// True if this band represents the rgb of the satellite image.
        /// </summary>
        public bool IsRgb
        {
            get { return _isRgb; }
            set { _isRgb = value; }
        }

        /// <summary>
        /// Whether this band is active or not.
        /// </summary>
        public bool IsVisible {
            get { return _isVisible;}
            set { _isVisible = value; OnPropertyChanged(nameof(IsVisible)); }
        }

        /// <summary>
        /// Whether this band is used as a feature in the classification or not.
        /// </summary>
        public bool IsFeature
        {
            get { return _isFeature; }
            set { _isFeature = value; OnPropertyChanged(nameof(IsFeature)); }
        }


        /// <summary>
        /// Whether this view model is enabled to be used as a feature.
        /// </summary>
        public bool CanChangeIsFeature
        {
            set {
                _canChangeIsFeature = value;
                OnPropertyChanged(nameof(CanChangeIsFeature));
            }
            get { return _canChangeIsFeature; }
        }

        public BandViewModel(string bandName, string bandPath, int bandNumber, double metersPerPixel, 
            WriteableBitmap bandImage, Matrix<double> transform, Vector<double> upperLeft, Vector<double> bottomRight, 
            int minCutScale, int maxCutScale, bool isRgb, bool isFeature = true, bool canChangeIsFeature = true)
        {
            MinCutScale = minCutScale;
            MaxCutScale = maxCutScale;

            Transform = transform;
            BandNumber = bandNumber;
            BandPath = bandPath;
            BandImage = bandImage;
            UpperLeft = upperLeft;
            BottomRight = bottomRight;

            BandName = bandName;
            MetersPerPixel = metersPerPixel;

            IsFeature = isFeature;
            CanChangeIsFeature = canChangeIsFeature;

            IsRgb = isRgb;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        [NotifyPropertyChangedInvocator]
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));        
        }
    }
}
