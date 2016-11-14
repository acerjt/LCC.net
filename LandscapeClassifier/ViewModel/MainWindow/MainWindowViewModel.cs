﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GalaSoft.MvvmLight;
using GalaSoft.MvvmLight.CommandWpf;
using GalaSoft.MvvmLight.Messaging;
using LandscapeClassifier.Annotations;
using LandscapeClassifier.Extensions;
using LandscapeClassifier.Model;
using LandscapeClassifier.Model.Classification;
using LandscapeClassifier.Util;
using LandscapeClassifier.View;
using LandscapeClassifier.View.Open;
using LandscapeClassifier.ViewModel.Dialogs;
using LandscapeClassifier.ViewModel.MainWindow.Classification;
using LandscapeClassifier.ViewModel.MainWindow.Prediction;
using MathNet.Numerics.LinearAlgebra;
using OSGeo.GDAL;

namespace LandscapeClassifier.ViewModel.MainWindow
{
    public class MainWindowViewModel : ViewModelBase
    {
        public ClassifierViewModel ClassifierViewModel { get; set; }
        public PredictionViewModel PredictionViewModel { get; set; }

        private bool _windowEnabled = true;

        /// <summary>
        /// The bands of the image.
        /// </summary>
        public ObservableCollection<LayerViewModel> Layers { get; set; }

        /// <summary>
        /// Exports a height map
        /// </summary>
        public ICommand ExportSmoothedHeightImageCommand { set; get; }

        /// <summary>
        /// Exports a slope map from the loaded DEM.
        /// </summary>
        public ICommand ExportSlopeImageCommand { set; get; }

        /// <summary>
        /// Exports a slope map from the loaded DEM.
        /// </summary>
        public ICommand ExportCurvatureImageCommand { set; get; }

        /// <summary>
        /// Create slope texture from a DEM.
        /// </summary>
        public ICommand CreateSlopeFromDEM { set; get; }

        /// <summary>
        /// Exit command.
        /// </summary>
        public ICommand ExitCommand { set; get; }

        /// <summary>
        /// Open image bands.
        /// </summary>
        public ICommand OpenSatelliteBandsCommand { set; get; }

        /// <summary>
        /// Add Layer
        /// </summary>
        public ICommand AddLayerCommand { set; get; }

        /// <summary>
        /// Block the main window.
        /// </summary>
        public bool WindowEnabled
        {
            get { return _windowEnabled; }
            set { _windowEnabled = value; RaisePropertyChanged(); }
        }

        public MainWindowViewModel()
        {
            GdalConfiguration.ConfigureGdal();
            ExitCommand = new RelayCommand(() => Application.Current.Shutdown(), () => true);

            // ExportSlopeImageCommand = new RelayCommand(() => ExportSlopeFromDEMDialog.ShowDialog(_ascFile), () => AscFile != null);
            // ExportCurvatureImageCommand = new RelayCommand(() => ExportCurvatureFromDEMDialog.ShowDialog(_ascFile), () => AscFile != null);
            // ExportSmoothedHeightImageCommand = new RelayCommand(() => ExportSmoothedDEMDialog.ShowDialog(_ascFile), () => AscFile != null);
            CreateSlopeFromDEM = new RelayCommand(() => new CreateSlopeFromDEMDialog().ShowDialog(), () => true);
            OpenSatelliteBandsCommand = new RelayCommand(AddBands, () => true);
            AddLayerCommand = new RelayCommand(AddLayers, () => true);
            Layers = new ObservableCollection<LayerViewModel>();

            ClassifierViewModel = new ClassifierViewModel(this);
            PredictionViewModel = new PredictionViewModel(this);
        }


        private void AddLayers()
        {
            AddLayerDialog dialog = new AddLayerDialog();

            if (dialog.ShowAddBandsDialog() == true && dialog.DialogViewModel.Layers.Count > 0)
            {
                AddLayers(dialog.DialogViewModel);
            }
        }

        private void AddBands()
        {
            AddBandsDialog dialog = new AddBandsDialog();

            if (dialog.ShowAddBandsDialog() == true && dialog.DialogViewModel.Bands.Count > 0)
            {
                AddBands(dialog.DialogViewModel);
            }
        }

        /// <summary>
        /// Adds the layers from the layer view model.
        /// </summary>
        /// <param name="viewModel"></param>
        public void AddLayers(AddLayerDialogViewModel viewModel)
        {
            try
            {
                WindowEnabled = false;

                Task.Factory.StartNew(() => Parallel.ForEach(viewModel.Layers, (layerInfo, _, bandIndex) =>
                {
                    var dataSet = Gdal.Open(layerInfo.Path, Access.GA_ReadOnly);
                    var rasterBand = dataSet.GetRasterBand(1);

                    // TODO differntiate between data types
                    int stride = (rasterBand.XSize * 32 + 7) / 8;
                    IntPtr data = Marshal.AllocHGlobal(stride * rasterBand.YSize);
                    rasterBand.ReadRaster(0, 0, rasterBand.XSize, rasterBand.YSize, data, rasterBand.XSize,
                        rasterBand.YSize, DataType.GDT_Float32, 4, stride);

                    // Cutoff
                    int minCutValue;
                    int maxCutValue;
                    CalculateMinMaxCut(rasterBand, out minCutValue, out maxCutValue);

                    double[] minMax = new double[2];
                    rasterBand.ComputeRasterMinMax(minMax, 0);
                    int min = (int)minMax[0];
                    int max = (int)minMax[1];

                    // Apply contrast enhancement
                    if (layerInfo.ContrastEnhancement)
                    {
                        data = ApplyContrastEnhancement(rasterBand, data, min, max);
                    }

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        var imageBandViewModel = CreateLayerViewModel(dataSet, rasterBand, stride, data,
                            PixelFormats.Gray32Float, Path.GetFileName(layerInfo.Path), layerInfo.Path, minCutValue, maxCutValue);

                        Layers.AddSorted(imageBandViewModel, Comparer<LayerViewModel>.Create(
                                  (a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal)));

                        Marshal.FreeHGlobal(data);

                        WindowEnabled = true;
                    });
                }));
            }
            catch
            {
                WindowEnabled = true;
            }
        }

        /// <summary>
        /// Adds the bands from the bands view model.
        /// </summary>
        /// <param name="viewModel"></param>
        public void AddBands(AddBandsDialogViewModel viewModel)
        {
            try
            {
                WindowEnabled = false;

                // Initialize RGB data
                byte[] bgra = null;
                Dataset rgbDataSet = null;
                if (viewModel.AddRgb)
                {
                    var firstRgbBand = viewModel.Bands.First(b => b.B || b.G || b.R);
                    rgbDataSet = Gdal.Open(firstRgbBand.Path, Access.GA_ReadOnly);
                    bgra = new byte[rgbDataSet.RasterXSize * rgbDataSet.RasterYSize * 4];
                }

                var firstBand = viewModel.Bands.First();
                var firstDataSet = Gdal.Open(firstBand.Path, Access.GA_ReadOnly);

                // Parallel band loading
                Task loadBands = Task.Factory.StartNew(() => Parallel.ForEach(viewModel.Bands, (bandInfo, _, bandIndex) =>
                    {
                        var dataSet = Gdal.Open(bandInfo.Path, Access.GA_ReadOnly);
                        var rasterBand = dataSet.GetRasterBand(1);
                        int stride = (rasterBand.XSize * 16 + 7) / 8;
                        IntPtr data = Marshal.AllocHGlobal(stride * rasterBand.YSize);
                        rasterBand.ReadRaster(0, 0, rasterBand.XSize, rasterBand.YSize, data, rasterBand.XSize,
                            rasterBand.YSize, DataType.GDT_UInt16, 2, stride);

                        // Cutoff
                        int minCutValue, maxCutValue;
                        CalculateMinMaxCut(rasterBand, out minCutValue, out maxCutValue);

                        // Add RGB
                        if (viewModel.AddRgb)
                        {
                            // Apply RGB contrast enhancement
                            if (viewModel.RgbContrastEnhancement && (bandInfo.B || bandInfo.G || bandInfo.R))
                            {
                                int colorOffset = bandInfo.B ? 0 : bandInfo.G ? 1 : bandInfo.R ? 2 : -1;
                                unsafe
                                {
                                    ushort* dataPtr = (ushort*)data.ToPointer();
                                    Parallel.ForEach(Partitioner.Create(0, rasterBand.XSize * rasterBand.YSize),
                                        (range) =>
                                        {
                                            for (int dataIndex = range.Item1; dataIndex < range.Item2; ++dataIndex)
                                            {
                                                ushort current = *(dataPtr + dataIndex);
                                                byte val = (byte)MoreMath.Clamp((current - minCutValue) / (double)(maxCutValue - minCutValue) * byte.MaxValue, 0, byte.MaxValue - 1);

                                                bgra[dataIndex * 4 + colorOffset] = val;
                                                bgra[dataIndex * 4 + 3] = 255;
                                            }
                                        });
                                }
                            }
                        }

                        // Apply band contrast enhancement
                        if (viewModel.BandContrastEnhancement)
                        {
                            data = ApplyContrastEnhancement(rasterBand, data, minCutValue, maxCutValue);
                        }


                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            string baneName = viewModel.SatelliteType.GetBand(Path.GetFileName(bandInfo.Path));
                            var imageBandViewModel = CreateLayerViewModel(dataSet, rasterBand, stride, data, PixelFormats.Gray16, baneName, bandInfo.Path, minCutValue, maxCutValue);

                            Layers.AddSorted(imageBandViewModel,
                                Comparer<LayerViewModel>.Create((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal)));

                            Marshal.FreeHGlobal(data);
                        });


                    }));

                // Load rgb image
                if (viewModel.AddRgb)
                {
                    var addRgb = loadBands.ContinueWith(t =>
                    {
                        // Create RGB image
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            var rgbStride = rgbDataSet.RasterXSize * 4;

                            var rgbImage = BitmapSource.Create(rgbDataSet.RasterXSize, rgbDataSet.RasterYSize, 96, 96,
                                PixelFormats.Bgra32, null, bgra,
                                rgbStride);

                            // Transformation
                            double[] rgbTransform = new double[6];
                            rgbDataSet.GetGeoTransform(rgbTransform);
                            var vecBuilder = Vector<double>.Build;
                            var upperLeft = vecBuilder.DenseOfArray(new[] { rgbTransform[0], rgbTransform[3], 1 });
                            var xRes = rgbTransform[1];
                            var yRes = rgbTransform[5];
                            var bottomRight =
                                vecBuilder.DenseOfArray(new[]
                                {
                                    upperLeft[0] + (rgbDataSet.RasterXSize*xRes),
                                    upperLeft[1] + (rgbDataSet.RasterYSize*yRes), 1
                                });


                            double[,] matArray =
                            {
                                {rgbTransform[1], rgbTransform[2], rgbTransform[0]},
                                {rgbTransform[4], rgbTransform[5], rgbTransform[3]},
                                {0, 0, 1}
                            };
                            var builder = Matrix<double>.Build;
                            var transformMat = builder.DenseOfArray(matArray);

                            Layers.Insert(0,
                                new LayerViewModel("RGB", null, xRes, yRes, new WriteableBitmap(rgbImage),
                                    transformMat, upperLeft, bottomRight, 0, 0, false,
                                    ClassifierViewModel.FeaturesViewModel.HasFeatures()));


                        });
                    });

                    // Send message
                    addRgb.ContinueWith(t =>
                    {
                        // TODO use projection to get correct transformation?
                        // Transformation
                        double[] transform = new double[6];
                        firstDataSet.GetGeoTransform(transform);
                        double[,] matArray =
                        {
                            {1, transform[2], transform[0]},
                            {transform[4], -1, transform[3]},
                            {0, 0, 1}
                        };
                        var builder = Matrix<double>.Build;
                        var transformMat = builder.DenseOfArray(matArray);

                        var message = new BandsLoadedMessage
                        {
                            SatelliteType = viewModel.SatelliteType,
                            RgbContrastEnhancement = viewModel.RgbContrastEnhancement,
                            AreBandsUnscaled = !viewModel.BandContrastEnhancement,
                            ProjectionName = firstDataSet.GetProjection(),
                            ScreenToWorld = transformMat,
                        };

                        Messenger.Default.Send(message);
                        WindowEnabled = true;
                    });
                }
                else
                {
                    WindowEnabled = true;
                }
            }
            catch
            {
                WindowEnabled = true;
            }
        }

        private LayerViewModel CreateLayerViewModel(Dataset dataSet, OSGeo.GDAL.Band rasterBand, int stride, IntPtr data, PixelFormat format, string name, string path, int minCutValue, int maxCutValue)
        {
            WriteableBitmap bandImage = new WriteableBitmap(rasterBand.XSize, rasterBand.YSize, 96, 96, format, null);
            bandImage.Lock();

            unsafe
            {
                Buffer.MemoryCopy(data.ToPointer(), bandImage.BackBuffer.ToPointer(),
                    stride * rasterBand.YSize,
                    stride * rasterBand.YSize);
            }

            bandImage.AddDirtyRect(new Int32Rect(0, 0, rasterBand.XSize, rasterBand.YSize));
            bandImage.Unlock();

            // Position
            double[] bandTransform = new double[6];
            dataSet.GetGeoTransform(bandTransform);
            var vecBuilder = Vector<double>.Build;
            var upperLeft = vecBuilder.DenseOfArray(new[] { bandTransform[0], bandTransform[3], 1 });
            var xRes = bandTransform[1];
            var yRes = bandTransform[5];
            var bottomRight = vecBuilder.DenseOfArray(new[] { upperLeft[0] + (rasterBand.XSize * xRes), upperLeft[1] + (rasterBand.YSize * yRes), 1 });
            double[,] matArray =
            {
                {bandTransform[1], bandTransform[2], bandTransform[0]},
                {bandTransform[4], bandTransform[5], bandTransform[3]},
                {0, 0, 1}
            };

            var builder = Matrix<double>.Build;
            var transformMat = builder.DenseOfArray(matArray);

            var imageBandViewModel = new LayerViewModel(
                name, path, xRes, yRes, bandImage, transformMat, upperLeft,
                bottomRight, minCutValue, maxCutValue, true,
                ClassifierViewModel.FeaturesViewModel.HasFeatures());

            return imageBandViewModel;
        }

        private static void CalculateMinMaxCut(OSGeo.GDAL.Band rasterBand, out int minCutValue, out int maxCutValue)
        {
            double[] minMax = new double[2];
            rasterBand.ComputeRasterMinMax(minMax, 0);
            int min = (int)minMax[0];
            int max = (int)minMax[1];
            int[] histogram = new int[max - min];
            rasterBand.GetHistogram(min, max, max - min, histogram, 1, 0,
                ProgressFunc, "");

            int totalPixels = 0;
            for (int bucket = 0; bucket < histogram.Length; ++bucket)
            {
                totalPixels += histogram[bucket];
            }

            double minCut = totalPixels * 0.02f;
            minCutValue = int.MaxValue;
            bool minCutSet = false;

            double maxCut = totalPixels * 0.98f;
            maxCutValue = 0;
            bool maxCutSet = false;

            int pixelCount = 0;
            for (int bucket = 0; bucket < histogram.Length; ++bucket)
            {
                pixelCount += histogram[bucket];
                if (pixelCount >= minCut && !minCutSet)
                {
                    minCutValue = bucket + min;
                    minCutSet = true;
                }
                if (pixelCount >= maxCut && !maxCutSet)
                {
                    maxCutValue = bucket + min;
                    maxCutSet = true;
                }
            }
        }

        private static unsafe IntPtr ApplyContrastEnhancement(OSGeo.GDAL.Band rasterBand, IntPtr data, int minCutValue, int maxCutValue)
        {
            Action<int> action;

            // Select appropriate 
            if (rasterBand.DataType == DataType.GDT_UInt16)
            {
                action = (int index) =>
                {
                    ushort* dataPtr = (ushort*)data.ToPointer();
                    ushort current = *(dataPtr + index);
                    *(dataPtr + index) = (ushort)MoreMath.Clamp((current - minCutValue) / (double)(maxCutValue - minCutValue) * ushort.MaxValue, 0, ushort.MaxValue);
                };
            }
            else if (rasterBand.DataType == DataType.GDT_Float32)
            {
                action = (int index) =>
                {
                    float* dataPtr = (float*)data.ToPointer();
                    float current = *(dataPtr + index);

                    *(dataPtr + index) = (float)MoreMath.Clamp((current - minCutValue) / (double)(maxCutValue - minCutValue) * 1.0f, 0.0f, 1.0f);
                };
            }
            else
            {
                throw new InvalidOperationException();
            }

            Parallel.ForEach(Partitioner.Create(0, rasterBand.XSize * rasterBand.YSize), (range) =>
                {
                    for (int dataIndex = range.Item1; dataIndex < range.Item2; ++dataIndex)
                    {
                        action(dataIndex);
                    }
                });

            return data;
        }

        private static int ProgressFunc(double complete, IntPtr message, IntPtr data)
        {
            return 1;
        }

    }

    public class BandsLoadedMessage
    {
        public SatelliteType SatelliteType { get; set; }
        public bool RgbContrastEnhancement { get; set; }
        public bool AreBandsUnscaled { get; set; }
        public string ProjectionName { get; set; }
        public Matrix<double> ScreenToWorld { get; set; }
    }

}