using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Navigation;
using System.Windows.Threading;
using OrchDesk.Core.Presentation;
using OrchDesk.Core.Research;
using OrchDesk.Core.Models;
using OrchDesk.Core.Automation;
using OrchDesk.App.ViewModels;
using Paragraph = OrchDesk.Core.Presentation.Paragraph;
using Table = OrchDesk.Core.Presentation.Table;

namespace OrchDesk.App.Views;

public sealed class MarkdownView : UserControl
{
	public static readonly DependencyProperty MarkdownProperty = DependencyProperty.Register("Markdown", typeof(string), typeof(MarkdownView), new PropertyMetadata((object)string.Empty, new PropertyChangedCallback(OnMarkdownChanged)));

	private static readonly FontFamily MonoFont = new FontFamily("Cascadia Mono, Consolas");

	private readonly StackPanel _panel = new StackPanel();

	private readonly List<UIElement> _elements = new List<UIElement>();

	private IReadOnlyList<MdBlock> _blocks = Array.Empty<MdBlock>();

	public string Markdown
	{
		get
		{
			return (string)((DependencyObject)this).GetValue(MarkdownProperty);
		}
		set
		{
			((DependencyObject)this).SetValue(MarkdownProperty, (object)value);
		}
	}

	public MarkdownView()
	{
		base.Content = _panel;
	}

	private static void OnMarkdownChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
	{
		if (d is MarkdownView markdownView)
		{
			markdownView.Refresh((e.NewValue as string) ?? string.Empty);
		}
	}

	private void Refresh(string text)
	{
		IReadOnlyList<MdBlock> readOnlyList = MarkdownParser.Parse(text);
		int i;
		for (i = 0; i < _blocks.Count && i < readOnlyList.Count && SameBlock(_blocks[i], readOnlyList[i]); i++)
		{
		}
		for (int num = _elements.Count - 1; num >= i; num--)
		{
			_panel.Children.RemoveAt(num);
			_elements.RemoveAt(num);
		}
		for (int j = i; j < readOnlyList.Count; j++)
		{
			FrameworkElement frameworkElement = RenderBlock(readOnlyList[j], j == 0);
			_elements.Add(frameworkElement);
			_panel.Children.Add(frameworkElement);
		}
		_blocks = readOnlyList;
	}

	private static bool SameBlock(MdBlock a, MdBlock b)
	{
		Heading? val = a as Heading;
		if (val != null)
		{
			Heading? val2 = b as Heading;
			if (val2 != null)
			{
				return val.Level == val2.Level && SameSpans(val.Spans, val2.Spans);
			}
		}
		else
		{
			Paragraph? val3 = a as Paragraph;
			if (val3 != null)
			{
				Paragraph? val4 = b as Paragraph;
				if (val4 != null)
				{
					return SameSpans(val3.Spans, val4.Spans);
				}
			}
			else
			{
				CodeBlock? val5 = a as CodeBlock;
				if (val5 != null)
				{
					CodeBlock? val6 = b as CodeBlock;
					if (val6 != null)
					{
						return val5.Language == val6.Language && val5.Code == val6.Code;
					}
				}
				else
				{
					ListBlock? val7 = a as ListBlock;
					if (val7 != null)
					{
						ListBlock? val8 = b as ListBlock;
						if (val8 != null)
						{
							return val7.Ordered == val8.Ordered && SameSeq(val7.Items, val8.Items, SameSpans);
						}
					}
					else
					{
						Quote? val9 = a as Quote;
						if (val9 != null)
						{
							Quote? val10 = b as Quote;
							if (val10 != null)
							{
								return SameSpans(val9.Spans, val10.Spans);
							}
						}
						else if (a is Rule)
						{
							if (b is Rule)
							{
								return true;
							}
						}
						else
						{
							Table? val11 = a as Table;
							if (val11 != null)
							{
								Table? val12 = b as Table;
								if (val12 != null)
								{
									return SameSeq(val11.Header, val12.Header, SameSpans) && SameSeq(val11.Rows, val12.Rows, (IReadOnlyList<IReadOnlyList<MdSpan>> r1, IReadOnlyList<IReadOnlyList<MdSpan>> r2) => SameSeq(r1, r2, SameSpans));
								}
							}
						}
					}
				}
			}
		}
		return false;
	}

	private static bool SameSpans(IReadOnlyList<MdSpan> a, IReadOnlyList<MdSpan> b)
	{
		return SameSeq(a, b, (MdSpan p, MdSpan q) => p == q);
	}

	private static bool SameSeq<T>(IReadOnlyList<T> a, IReadOnlyList<T> b, Func<T, T, bool> eq)
	{
		if (a.Count != b.Count)
		{
			return false;
		}
		for (int i = 0; i < a.Count; i++)
		{
			if (!eq(a[i], b[i]))
			{
				return false;
			}
		}
		return true;
	}

	private FrameworkElement RenderBlock(MdBlock block, bool first)
	{
		Heading? val = block as Heading;
		FrameworkElement frameworkElement;
		if (val == null)
		{
			Paragraph? val2 = block as Paragraph;
			if (val2 == null)
			{
				CodeBlock? val3 = block as CodeBlock;
				if (val3 == null)
				{
					ListBlock? val4 = block as ListBlock;
					if (val4 == null)
					{
						Quote? val5 = block as Quote;
						if (val5 == null)
						{
							if (!(block is Rule))
							{
								Table? val6 = block as Table;
								frameworkElement = ((val6 == null) ? Body(Array.Empty<MdSpan>()) : RenderTable(val6));
							}
							else
							{
								frameworkElement = RenderRule();
							}
						}
						else
						{
							frameworkElement = RenderQuote(val5);
						}
					}
					else
					{
						frameworkElement = RenderList(val4);
					}
				}
				else
				{
					frameworkElement = RenderCode(val3);
				}
			}
			else
			{
				frameworkElement = Body(val2.Spans);
			}
		}
		else
		{
			frameworkElement = RenderHeading(val);
		}
		FrameworkElement frameworkElement2 = frameworkElement;
		frameworkElement2.Margin = new Thickness(0.0, (!first) ? 10 : 0, 0.0, 0.0);
		return frameworkElement2;
	}

	private static TextBlock Body(IReadOnlyList<MdSpan> spans, string brushKey = "TextBrush")
	{
		TextBlock textBlock = new TextBlock();
		textBlock.TextWrapping = TextWrapping.Wrap;
		textBlock.FontSize = 13.5;
		textBlock.LineHeight = 20.0;
		textBlock.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
		FillInlines(textBlock.Inlines, spans);
		return textBlock;
	}

	private static FrameworkElement RenderHeading(Heading h)
	{
		double num = h.Level switch
		{
			1 => 22, 
			2 => 18, 
			3 => 16, 
			4 => 15, 
			_ => 14, 
		};
		TextBlock textBlock = new TextBlock();
		textBlock.TextWrapping = TextWrapping.Wrap;
		textBlock.FontSize = num;
		textBlock.FontWeight = FontWeights.SemiBold;
		textBlock.LineHeight = num + 8.0;
		textBlock.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
		FillInlines(textBlock.Inlines, h.Spans);
		return textBlock;
	}

	private static FrameworkElement RenderQuote(Quote q)
	{
		DockPanel obj = new DockPanel
		{
			LastChildFill = true
		};
		Border border = new Border
		{
			Width = 3.0,
			Margin = new Thickness(0.0, 0.0, 10.0, 0.0)
		};
		border.SetResourceReference(Border.BackgroundProperty, "BorderBrush");
		DockPanel.SetDock(border, Dock.Left);
		obj.Children.Add(border);
		obj.Children.Add(Body(q.Spans, "MutedBrush"));
		return obj;
	}

	private static FrameworkElement RenderList(ListBlock l)
	{
		StackPanel stackPanel = new StackPanel();
		for (int i = 0; i < l.Items.Count; i++)
		{
			TextBlock textBlock = new TextBlock
			{
				Text = (l.Ordered ? $"{i + 1}." : "•"),
				FontSize = 13.5,
				LineHeight = 20.0,
				Margin = new Thickness(0.0, 0.0, 8.0, 0.0),
				MinWidth = (l.Ordered ? 18 : 0)
			};
			textBlock.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
			DockPanel dockPanel = new DockPanel();
			DockPanel.SetDock(textBlock, Dock.Left);
			dockPanel.Children.Add(textBlock);
			dockPanel.Children.Add(Body(l.Items[i]));
			if (i > 0)
			{
				dockPanel.Margin = new Thickness(0.0, 2.0, 0.0, 0.0);
			}
			stackPanel.Children.Add(dockPanel);
		}
		return stackPanel;
	}

	private static FrameworkElement RenderRule()
	{
		Border border = new Border();
		border.Height = 1.0;
		border.SetResourceReference(Border.BackgroundProperty, "BorderBrush");
		return border;
	}

	private static FrameworkElement RenderCode(CodeBlock c)
	{
		string code = c.Code;
		Border border = new Border
		{
			CornerRadius = new CornerRadius(8.0),
			BorderThickness = new Thickness(1.0)
		};
		border.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
		border.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
		TextBlock copyLabel = new TextBlock
		{
			Text = "Copy",
			FontSize = 11.5
		};
		copyLabel.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
		Button button = new Button
		{
			Content = copyLabel,
			Padding = new Thickness(8.0, 2.0, 8.0, 2.0),
			Background = Brushes.Transparent,
			BorderThickness = new Thickness(0.0),
			Focusable = false,
			HorizontalAlignment = HorizontalAlignment.Right
		};
		DockPanel.SetDock(button, Dock.Right);
		TextBlock textBlock = new TextBlock
		{
			Text = c.Language,
			FontSize = 11.5,
			VerticalAlignment = VerticalAlignment.Center
		};
		textBlock.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
		DockPanel dockPanel = new DockPanel
		{
			Margin = new Thickness(10.0, 4.0, 6.0, 2.0)
		};
		dockPanel.Children.Add(button);
		dockPanel.Children.Add(textBlock);
		DispatcherTimer? resetTimer = null;
		button.Click += delegate
		{
			try
			{
				Clipboard.SetText(code);
			}
			catch (Exception)
			{
				return;
			}
			copyLabel.Text = "Copied";
			DispatcherTimer? obj = resetTimer;
			if (obj != null)
			{
				obj.Stop();
			}
			resetTimer = new DispatcherTimer
			{
				Interval = TimeSpan.FromSeconds(1.5)
			};
			resetTimer.Tick += delegate
			{
				copyLabel.Text = "Copy";
				DispatcherTimer obj2 = resetTimer;
				if (obj2 != null)
				{
					obj2.Stop();
				}
			};
			resetTimer.Start();
		};
		TextBlock textBlock2 = new TextBlock
		{
			FontFamily = MonoFont,
			FontSize = 12.5,
			TextWrapping = TextWrapping.NoWrap,
			Margin = new Thickness(10.0, 4.0, 10.0, 10.0)
		};
		textBlock2.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
		FillInlines(textBlock2.Inlines, (IEnumerable<MdSpan>)(object)new MdSpan[1]
		{
			new MdSpan(code, false, false, false, null)
		});
		ScrollViewer element = new ScrollViewer
		{
			HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
			VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
			Content = textBlock2
		};
		StackPanel stackPanel = new StackPanel();
		stackPanel.Children.Add(dockPanel);
		stackPanel.Children.Add(element);
		border.Child = stackPanel;
		return border;
	}

	private static FrameworkElement RenderTable(OrchDesk.Core.Presentation.Table t)
	{
		int val = Math.Max(t.Header.Count, (t.Rows.Count != 0) ? t.Rows.Max((IReadOnlyList<IReadOnlyList<MdSpan>> r) => r.Count) : 0);
		val = Math.Max(val, 1);
		int num = 1 + t.Rows.Count;
		Grid grid = new Grid();
		for (int i = 0; i < val; i++)
		{
			grid.ColumnDefinitions.Add(new ColumnDefinition
			{
				Width = new GridLength(1.0, GridUnitType.Star),
				MinWidth = 60.0
			});
		}
		for (int j = 0; j < num; j++)
		{
			grid.RowDefinitions.Add(new RowDefinition
			{
				Height = GridLength.Auto
			});
		}
		for (int k = 0; k < num; k++)
		{
			bool flag = k == 0;
			IReadOnlyList<IReadOnlyList<MdSpan>> readOnlyList = (flag ? t.Header : t.Rows[k - 1]);
			for (int l = 0; l < val; l++)
			{
				IReadOnlyList<MdSpan> spans;
				if (l >= readOnlyList.Count)
				{
					IReadOnlyList<MdSpan> readOnlyList2 = Array.Empty<MdSpan>();
					spans = readOnlyList2;
				}
				else
				{
					spans = readOnlyList[l];
				}
				TextBlock textBlock = Body(spans);
				if (flag)
				{
					textBlock.FontWeight = FontWeights.SemiBold;
				}
				Border border = new Border
				{
					BorderThickness = new Thickness(0.0, 0.0, 1.0, 1.0),
					Padding = new Thickness(8.0, 4.0, 8.0, 4.0),
					Child = textBlock
				};
				border.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
				if (flag)
				{
					border.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
				}
				Grid.SetRow(border, k);
				Grid.SetColumn(border, l);
				grid.Children.Add(border);
			}
		}
		Border border2 = new Border();
		border2.BorderThickness = new Thickness(1.0, 1.0, 0.0, 0.0);
		border2.Child = grid;
		border2.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
		return border2;
	}

	private static void FillInlines(InlineCollection inlines, IEnumerable<MdSpan> spans)
	{
		foreach (MdSpan span in spans)
		{
			string[] array = span.Text.Split('\n');
			for (int i = 0; i < array.Length; i++)
			{
				if (i > 0)
				{
					inlines.Add(new LineBreak());
				}
				if (array[i].Length != 0)
				{
					inlines.Add(MakeInline(span, array[i]));
				}
			}
		}
	}

	private static Inline MakeInline(MdSpan span, string text)
	{
		Run run = new Run(text);
		if (span.Bold)
		{
			run.FontWeight = FontWeights.Bold;
		}
		if (span.Italic)
		{
			run.FontStyle = FontStyles.Italic;
		}
		if (span.Code)
		{
			run.FontFamily = MonoFont;
			run.FontSize = 12.5;
			run.SetResourceReference(TextElement.BackgroundProperty, "SurfaceBrush");
		}
		if (span.Link != null)
		{
			if (Uri.TryCreate(span.Link, UriKind.Absolute, out Uri? uri) && IsWebUri(uri))
			{
				Hyperlink hyperlink = new Hyperlink(run);
				hyperlink.NavigateUri = uri;
				hyperlink.SetResourceReference(TextElement.ForegroundProperty, "AccentBrush");
				hyperlink.RequestNavigate += delegate(object _, RequestNavigateEventArgs e)
				{
					OpenWebUri(uri);
					e.Handled = true;
				};
				return hyperlink;
			}
			run.SetResourceReference(TextElement.ForegroundProperty, "AccentBrush");
		}
		return run;
	}

	private static bool IsWebUri(Uri uri)
	{
		if (!(uri.Scheme == Uri.UriSchemeHttp))
		{
			return uri.Scheme == Uri.UriSchemeHttps;
		}
		return true;
	}

	private static void OpenWebUri(Uri uri)
	{
		if (!IsWebUri(uri))
		{
			return;
		}
		try
		{
			Process.Start(new ProcessStartInfo(uri.AbsoluteUri)
			{
				UseShellExecute = true
			});
		}
		catch (Exception)
		{
		}
	}
}
