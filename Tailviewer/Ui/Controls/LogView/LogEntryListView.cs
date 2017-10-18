﻿using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using log4net;
using Metrolib.Controls;
using Tailviewer.BusinessLogic;
using Tailviewer.BusinessLogic.DataSources;
using Tailviewer.BusinessLogic.LogFiles;
using Tailviewer.BusinessLogic.Searches;
using Tailviewer.Core;
using Tailviewer.Settings;

namespace Tailviewer.Ui.Controls.LogView
{
	public class LogEntryListView
		: Grid
		, ILogFileListener
	{
		private static readonly ILog Log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

		public static readonly DependencyProperty DataSourceProperty =
			DependencyProperty.Register("DataSource", typeof(IDataSource), typeof(LogEntryListView),
				new PropertyMetadata(propertyChangedCallback: null));

		public static readonly DependencyProperty LogFileProperty =
			DependencyProperty.Register("LogFile", typeof(ILogFile), typeof(LogEntryListView),
				new PropertyMetadata(defaultValue: null, propertyChangedCallback: OnLogFileChanged));

		public static readonly DependencyProperty SearchProperty =
			DependencyProperty.Register("Search", typeof(ILogFileSearch), typeof(LogEntryListView),
				new PropertyMetadata(defaultValue: null, propertyChangedCallback: OnSearchChanged));

		public static readonly DependencyProperty FollowTailProperty =
			DependencyProperty.Register("FollowTail", typeof(bool), typeof(LogEntryListView),
				new PropertyMetadata(defaultValue: false, propertyChangedCallback: OnFollowTailChanged));

		public static readonly DependencyProperty ShowLineNumbersProperty =
			DependencyProperty.Register("ShowLineNumbers", typeof(bool), typeof(LogEntryListView),
				new PropertyMetadata(defaultValue: true, propertyChangedCallback: OnShowLineNumbersChanged));

		public static readonly DependencyProperty CurrentLineProperty =
			DependencyProperty.Register("CurrentLine", typeof(LogLineIndex), typeof(LogEntryListView),
				new PropertyMetadata(default(LogLineIndex), OnCurrentLineChanged));

		public static readonly DependencyProperty ColorByLevelProperty =
			DependencyProperty.Register("ColorByLevel", typeof(bool), typeof(LogEntryListView),
				new PropertyMetadata(defaultValue: false, propertyChangedCallback: OnColorByLevelChanged));

		public static readonly DependencyProperty MergedDataSourceDisplayModeProperty = DependencyProperty.Register(
			"MergedDataSourceDisplayMode", typeof(DataSourceDisplayMode), typeof(LogEntryListView),
			new PropertyMetadata(default(DataSourceDisplayMode), OnMergedDataSourceDisplayModeChanged));

		public static readonly TimeSpan MaximumRefreshInterval = TimeSpan.FromMilliseconds(value: 33);
		private readonly DataSourceCanvas _dataSourceCanvas;
		private readonly FlatScrollBar _horizontalScrollBar;

		private readonly LineNumberCanvas _lineNumberCanvas;
		private readonly DispatcherTimer _timer;
		private readonly FlatScrollBar _verticalScrollBar;

		private int _maxLineWidth;
		private int _pendingModificationsCount;

		static LogEntryListView()
		{
			DefaultStyleKeyProperty.OverrideMetadata(typeof(LogEntryListView),
				new FrameworkPropertyMetadata(typeof(LogEntryListView)));
		}

		public LogEntryListView()
		{
			ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(value: 1, type: GridUnitType.Auto) });
			ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(value: 1, type: GridUnitType.Auto) });
			ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(value: 1, type: GridUnitType.Auto) });
			ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(value: 1, type: GridUnitType.Star) });
			ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(value: 1, type: GridUnitType.Auto) });
			RowDefinitions.Add(new RowDefinition { Height = new GridLength(value: 1, type: GridUnitType.Star) });
			RowDefinitions.Add(new RowDefinition { Height = new GridLength(value: 1, type: GridUnitType.Auto) });

			_verticalScrollBar = new FlatScrollBar
			{
				Name = "PART_VerticalScrollBar",
				Thickness = 18
			};
			_verticalScrollBar.ValueChanged += VerticalScrollBarOnValueChanged;
			_verticalScrollBar.Scroll += VerticalScrollBarOnScroll;
			_verticalScrollBar.SetValue(RowProperty, value: 0);
			_verticalScrollBar.SetValue(ColumnProperty, value: 4);
			_verticalScrollBar.SetValue(RangeBase.SmallChangeProperty, TextHelper.LineHeight);
			_verticalScrollBar.SetValue(RangeBase.LargeChangeProperty, 10 * TextHelper.LineHeight);

			_horizontalScrollBar = new FlatScrollBar
			{
				Name = "PART_HorizontalScrollBar",
				Orientation = Orientation.Horizontal,
				Thickness = 18
			};
			_horizontalScrollBar.SetValue(RowProperty, value: 1);
			_horizontalScrollBar.SetValue(ColumnProperty, value: 0);
			_horizontalScrollBar.SetValue(ColumnSpanProperty, value: 4);
			_horizontalScrollBar.SetValue(RangeBase.SmallChangeProperty, TextHelper.LineHeight);
			_horizontalScrollBar.SetValue(RangeBase.LargeChangeProperty, 10 * TextHelper.LineHeight);

			PartTextCanvas = new TextCanvas(_horizontalScrollBar, _verticalScrollBar);
			PartTextCanvas.SetValue(RowProperty, value: 0);
			PartTextCanvas.SetValue(ColumnProperty, value: 3);
			PartTextCanvas.MouseWheelDown += TextCanvasOnMouseWheelDown;
			PartTextCanvas.MouseWheelUp += TextCanvasOnMouseWheelUp;
			PartTextCanvas.SizeChanged += TextCanvasOnSizeChanged;
			PartTextCanvas.VisibleLinesChanged += TextCanvasOnVisibleLinesChanged;
			PartTextCanvas.RequestBringIntoView += TextCanvasOnRequestBringIntoView;
			PartTextCanvas.VisibleSectionChanged += TextCanvasOnVisibleSectionChanged;
			PartTextCanvas.OnSelectionChanged += TextCanvasOnOnSelectionChanged;

			_lineNumberCanvas = new LineNumberCanvas();
			_lineNumberCanvas.SetValue(RowProperty, value: 0);
			_lineNumberCanvas.SetValue(ColumnProperty, value: 0);
			_lineNumberCanvas.SetValue(MarginProperty, new Thickness(left: 5, top: 0, right: 5, bottom: 0));

			_dataSourceCanvas = new DataSourceCanvas();
			_dataSourceCanvas.SetValue(RowProperty, value: 0);
			_dataSourceCanvas.SetValue(ColumnProperty, value: 1);
			_dataSourceCanvas.SetValue(MarginProperty, new Thickness(left: 0, top: 0, right: 5, bottom: 0));

			var separator = new Rectangle
			{
				Fill = new SolidColorBrush(Color.FromRgb(225, 228, 232)),
				Width = 2
			};
			separator.SetValue(RowProperty, value: 0);
			separator.SetValue(ColumnProperty, value: 2);
			separator.SetValue(MarginProperty, new Thickness(left: 0, top: 0, right: 5, bottom: 0));

			Children.Add(_lineNumberCanvas);
			Children.Add(_dataSourceCanvas);
			Children.Add(separator);
			Children.Add(PartTextCanvas);
			Children.Add(_verticalScrollBar);
			Children.Add(_horizontalScrollBar);

			_timer = new DispatcherTimer(MaximumRefreshInterval, DispatcherPriority.Normal, OnTimer, Dispatcher);
			_timer.Start();
		}

		public DataSourceDisplayMode MergedDataSourceDisplayMode
		{
			get { return (DataSourceDisplayMode) GetValue(MergedDataSourceDisplayModeProperty); }
			set { SetValue(MergedDataSourceDisplayModeProperty, value); }
		}

		public bool ColorByLevel
		{
			get { return (bool) GetValue(ColorByLevelProperty); }
			set { SetValue(ColorByLevelProperty, value); }
		}

		public LogLineIndex CurrentLine
		{
			get { return (LogLineIndex) GetValue(CurrentLineProperty); }
			set { SetValue(CurrentLineProperty, value); }
		}

		public bool ShowLineNumbers
		{
			get { return (bool) GetValue(ShowLineNumbersProperty); }
			set { SetValue(ShowLineNumbersProperty, value); }
		}

		public bool FollowTail
		{
			get { return (bool) GetValue(FollowTailProperty); }
			set { SetValue(FollowTailProperty, value); }
		}

		public int PendingModificationsCount => _pendingModificationsCount;

		public ScrollBar VerticalScrollBar => _verticalScrollBar;

		public ScrollBar HorizontalScrollBar => _horizontalScrollBar;

		public IDataSource DataSource
		{
			get { return (IDataSource) GetValue(DataSourceProperty); }
			set { SetValue(DataSourceProperty, value); }
		}

		public ILogFile LogFile
		{
			get { return (ILogFile) GetValue(LogFileProperty); }
			set { SetValue(LogFileProperty, value); }
		}

		public ILogFileSearch Search
		{
			get { return (ILogFileSearch) GetValue(SearchProperty); }
			set { SetValue(SearchProperty, value); }
		}

		public List<TextLine> VisibleTextLines => PartTextCanvas.VisibleTextLines;

		public IEnumerable<LogLineIndex> SelectedIndices
		{
			get { return PartTextCanvas.SelectedIndices; }
			set { PartTextCanvas.SelectedIndices = value; }
		}

		public TextCanvas PartTextCanvas { get; }

		public int SelectedSearchResultIndex
		{
			get { return PartTextCanvas.SelectedSearchResultIndex; }
			set { PartTextCanvas.SelectedSearchResultIndex = value; }
		}

		public void OnLogFileModified(ILogFile logFile, LogFileSection section)
		{
			var width = TextHelper.EstimateWidthUpperLimit(logFile.MaxCharactersPerLine);
			var upperWidth = (int) Math.Ceiling(width);

			// Setting an integer is an atomic operation and thus no
			// special synchronization is required.
			_maxLineWidth = Math.Max(_maxLineWidth, upperWidth);

			Interlocked.Increment(ref _pendingModificationsCount);
		}

		private static void OnMergedDataSourceDisplayModeChanged(DependencyObject dependencyObject,
			DependencyPropertyChangedEventArgs args)
		{
			((LogEntryListView) dependencyObject).OnMergedDataSourceDisplayModeChanged((DataSourceDisplayMode) args.NewValue);
		}

		private void OnMergedDataSourceDisplayModeChanged(DataSourceDisplayMode displayMode)
		{
			_dataSourceCanvas.DisplayMode = displayMode;
		}

		private static void OnSearchChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
		{
			((LogEntryListView) dependencyObject).OnSearchChanged((ILogFileSearch) args.NewValue);
		}

		private void OnSearchChanged(ILogFileSearch search)
		{
			PartTextCanvas.Search = search;
		}

		private static void OnColorByLevelChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
		{
			((LogEntryListView) dependencyObject).OnColorByLevelChanged((bool) args.NewValue);
		}

		private void OnColorByLevelChanged(bool colorByLevel)
		{
			PartTextCanvas.ColorByLevel = colorByLevel;
		}

		private static void OnCurrentLineChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
		{
			((LogEntryListView) dependencyObject).OnCurrentLineChanged((LogLineIndex) args.NewValue);
		}

		private static void OnShowLineNumbersChanged(DependencyObject dependencyObject,
			DependencyPropertyChangedEventArgs args)
		{
			((LogEntryListView) dependencyObject).OnShowLineNumbersChanged((bool) args.NewValue);
		}

		private void OnShowLineNumbersChanged(bool showLineNumbers)
		{
			_lineNumberCanvas.Visibility = showLineNumbers
				? Visibility.Visible
				: Visibility.Collapsed;
		}

		private void OnCurrentLineChanged(LogLineIndex index)
		{
			var current = PartTextCanvas.CurrentlyVisibleSection;
			if (index != LogLineIndex.Invalid)
			{
				PartTextCanvas.CurrentLine = (int) index;
				//< We don't want to call BringIntoView() everytime because that one scrolls to fully
				// bring a line into view. This would mean that if the currently visible section changed
				// so that the top line is only partially visible, then this method would always bring
				// it fully visible. Removing the following filter would completely remove per pixel scrolling...
				if (current.Index != index)
					_verticalScrollBar.Value = (int) index * TextHelper.LineHeight;
			}
		}

		private void TextCanvasOnVisibleSectionChanged(LogFileSection section)
		{
			CurrentLine = section.Index;
		}

		private void TextCanvasOnOnSelectionChanged(HashSet<LogLineIndex> selectedIndices)
		{
			SelectionChanged?.Invoke(selectedIndices);
		}

		public event Action<IEnumerable<LogLineIndex>> SelectionChanged;

		private void TextCanvasOnRequestBringIntoView(LogLineIndex logLineIndex, LogLineMatch match)
		{
			BringIntoView(logLineIndex, match);
		}

		public void BringIntoView(LogLineIndex logLineIndex, LogLineMatch match = new LogLineMatch())
		{
			var height = PartTextCanvas.ActualHeight;
			var offset = PartTextCanvas.YOffset;
			var start = PartTextCanvas.CurrentLine;
			var diff = logLineIndex - start;
			var minY = diff * TextHelper.LineHeight + offset;
			var maxY = (diff + 1) * TextHelper.LineHeight + offset;
			if (minY < 0)
				_verticalScrollBar.Value += minY;
			else if (maxY > height)
				_verticalScrollBar.Value += maxY - height;

			var minX = TextHelper.GlyphWidth * match.Index;
			var maxX = TextHelper.GlyphWidth * (match.Index + match.Count);
			var visibleMinX = _horizontalScrollBar.Value;
			var visibleMaxX = visibleMinX + _horizontalScrollBar.ViewportSize;
			if (maxX < visibleMinX || minX > visibleMaxX)
				_horizontalScrollBar.Value = minX;

			var logFile = LogFile;
			if (logFile != null)
			{
				var count = logFile.Count;
				if (count > 0)
					FollowTail = logLineIndex >= count - 1;
			}
		}

		private void TextCanvasOnVisibleLinesChanged()
		{
			_lineNumberCanvas.UpdateLineNumbers(LogFile,
				PartTextCanvas.CurrentlyVisibleSection,
				PartTextCanvas.YOffset);
			_dataSourceCanvas.UpdateDataSources(DataSource,
				PartTextCanvas.CurrentlyVisibleSection,
				PartTextCanvas.YOffset);
		}

		private void TextCanvasOnSizeChanged(object sender, SizeChangedEventArgs sizeChangedEventArgs)
		{
			UpdateScrollViewerRegions();
		}

		public void TextCanvasOnMouseWheelUp()
		{
			var delta = _verticalScrollBar.Value - _verticalScrollBar.Minimum;
			var toScroll = Math.Min(delta, TextHelper.LineHeight);

			if (toScroll > 0)
			{
				FollowTail = false;
				_verticalScrollBar.Value -= toScroll;
			}
		}

		public void TextCanvasOnMouseWheelDown()
		{
			var delta = _verticalScrollBar.Maximum - _verticalScrollBar.Value;
			var toScroll = Math.Min(delta, TextHelper.LineHeight);
			_verticalScrollBar.Value += toScroll;

			if (_verticalScrollBar.Value >= _verticalScrollBar.Maximum)
				FollowTail = true;
		}

		private static void OnFollowTailChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
		{
			((LogEntryListView) d).OnFollowTailChanged((bool) e.NewValue);
		}

		public event Action<bool> FollowTailChanged;

		private void OnFollowTailChanged(bool followTail)
		{
			if (followTail)
				ScrollToBottom();

			FollowTailChanged?.Invoke(followTail);
		}

		private void ScrollToBottom()
		{
			_verticalScrollBar.Value = _verticalScrollBar.Maximum;
		}

		public void OnTimer(object sender, EventArgs args)
		{
			if (Interlocked.Exchange(ref _pendingModificationsCount, value: 0) > 0)
				try
				{
					PartTextCanvas.DetermineVerticalOffset();
					PartTextCanvas.CurrentlyVisibleSection = CalculateVisibleSection();

					UpdateScrollViewerRegions();
					ScrollToBottomIfRequired();

					PartTextCanvas.UpdateVisibleLines();
					PartTextCanvas.OnMouseMove();
				}
				catch (Exception e)
				{
					Log.ErrorFormat("Caught unexpected exception while updating: {0}", e);

					// Let's pray that this was just a hickup and try next time...
					Interlocked.Increment(ref _pendingModificationsCount);
				}
		}

		private void ScrollToBottomIfRequired()
		{
			if (FollowTail)
				ScrollToBottom();
		}

		/// <summary>
		///     The section of the log file that is currently visible.
		/// </summary>
		[Pure]
		public LogFileSection CalculateVisibleSection()
		{
			return PartTextCanvas.CalculateVisibleSection();
		}

		private static void OnLogFileChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
		{
			((LogEntryListView) d).OnLogFileChanged(e.OldValue as ILogFile, e.NewValue as ILogFile);
		}

		private void OnLogFileChanged(ILogFile oldValue, ILogFile newValue)
		{
			oldValue?.RemoveListener(this);

			PartTextCanvas.LogFile = newValue;

			if (newValue != null)
			{
				newValue.AddListener(this, TimeSpan.FromMilliseconds(value: 100), maximumLineCount: 10000);

				_maxLineWidth = (int) Math.Ceiling(TextHelper.EstimateWidthUpperLimit(newValue.MaxCharactersPerLine));
				UpdateScrollViewerRegions();
				PartTextCanvas.DetermineVerticalOffset();
				PartTextCanvas.UpdateVisibleSection();
				PartTextCanvas.UpdateVisibleLines();
			}
			else
			{
				_maxLineWidth = 0;
			}

			MatchScrollbarValueToCurrentLine();
		}

		private void MatchScrollbarValueToCurrentLine()
		{
			var value = PartTextCanvas.CurrentLine * TextHelper.LineHeight;
			if (value >= 0 && value <= _verticalScrollBar.Maximum)
				_verticalScrollBar.Value = value;
			else
				_verticalScrollBar.Value = 0;
		}

		private void UpdateScrollViewerRegions()
		{
			if (LogFile == null)
			{
			}
			else
			{
				//
				// Depending on the current size of the logfile and the size of the viewport,
				// we need to determine the range of our scrollers.
				//

				UpdateHorizontalScrollbar();
				UpdateVerticalScrollbar();
			}
		}

		private void UpdateHorizontalScrollbar()
		{
			double totalWidth = _maxLineWidth;
			var usableWidth = PartTextCanvas.ActualWidth;
			if (totalWidth > usableWidth)
			{
				_horizontalScrollBar.Minimum = 0;
				_horizontalScrollBar.Maximum = totalWidth - usableWidth;
				_horizontalScrollBar.ViewportSize = usableWidth;
				_horizontalScrollBar.Visibility = Visibility.Visible;
			}
			else
			{
				_horizontalScrollBar.Minimum = 0;
				_horizontalScrollBar.Maximum = 0;
				_horizontalScrollBar.ViewportSize = usableWidth;
				_horizontalScrollBar.Visibility = Visibility.Collapsed;
			}
		}

		private void UpdateVerticalScrollbar()
		{
			var count = LogFile.Count;
			var totalHeight = count * TextHelper.LineHeight;
			var usableHeight = PartTextCanvas.ActualHeight;
			if (totalHeight > usableHeight)
			{
				_verticalScrollBar.Minimum = 0;
				_verticalScrollBar.Maximum = Math.Ceiling((totalHeight - usableHeight) / TextHelper.LineHeight) *
				                             TextHelper.LineHeight;
				_verticalScrollBar.ViewportSize = usableHeight;
				_verticalScrollBar.Visibility = Visibility.Visible;
			}
			else
			{
				_verticalScrollBar.Minimum = 0;
				_verticalScrollBar.Maximum = 0;
				_verticalScrollBar.ViewportSize = usableHeight;
				_verticalScrollBar.Visibility = Visibility.Collapsed;
			}
		}

		private void VerticalScrollBarOnScroll(object sender, ScrollEventArgs scrollEventArgs)
		{
			if (_lastScroll.NewValue < _lastScroll.OldValue)
				FollowTail = false;
			if (scrollEventArgs.NewValue >= _verticalScrollBar.Maximum)
				FollowTail = true;
		}

		private void VerticalScrollBarOnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> args)
		{
			_lastScroll = new ScrollEvent
			{
				OldValue = args.OldValue,
				NewValue = args.NewValue
			};
		}

		public void Select(LogLineIndex index)
		{
			PartTextCanvas.SetSelected(index, SelectMode.Replace);
		}

		public void Select(IEnumerable<LogLineIndex> indices)
		{
			PartTextCanvas.SetSelected(indices, SelectMode.Replace);
		}

		public void Select(params LogLineIndex[] indices)
		{
			PartTextCanvas.SetSelected(indices, SelectMode.Replace);
		}

		public void CopySelectedLinesToClipboard()
		{
			PartTextCanvas.OnCopyToClipboard();
		}

		public void SetHorizontalOffset(double value)
		{
			if (value < _horizontalScrollBar.Minimum)
				_horizontalScrollBar.Value = _horizontalScrollBar.Minimum;
			else if (value > _horizontalScrollBar.Maximum)
				_horizontalScrollBar.Value = _horizontalScrollBar.Maximum;
			else
				_horizontalScrollBar.Value = value;
		}

		#region Scrolling

		private ScrollEvent _lastScroll;

		private struct ScrollEvent
		{
			public double NewValue;
			public double OldValue;
		}

		#endregion
	}
}