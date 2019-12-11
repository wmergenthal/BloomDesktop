using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net.Mail;
using System.Text;
using System.Windows.Forms;
using Bloom.Api;
using Bloom.Book;
using Bloom.MiscUI;
using Bloom.ToPalaso;
using SIL.IO;
using SIL.Reporting;

namespace Bloom.web.controllers
{
	internal class ProblemReportApi : IDisposable
	{
		private readonly BookSelection _bookSelection;
		private static TempFile _screenshotTempFile;
		private BloomZipFile _bookZipFile;
		private readonly TempFile _bookZipFileTemp;
		protected string YouTrackProjectKey = "BL";
		private static Exception _currentException;
		private static string _detailedMessage; // usually from Bloom itself

		private string CollectionFolder => Path.GetDirectoryName(_bookSelection.CurrentSelection.StoragePageFolder);

		public ProblemReportApi(BookSelection bookSelection)
		{
			_bookSelection = bookSelection;
			_bookZipFileTemp = TempFile.WithFilenameInTempFolder("bookData.zip");
		}

		public void RegisterWithApiHandler(BloomApiHandler apiHandler)
		{
			// ProblemDialog.tsx uses this endpoint to get the screenshot image.
			apiHandler.RegisterEndpointHandler("problemReport/screenshot",
				(ApiRequest request) =>
				{
					request.ReplyWithImage(_screenshotTempFile.Path); 
				}, true);

			// ProblemDialog.tsx uses this endpoint to get the name of the book.
			apiHandler.RegisterEndpointHandler("problemReport/bookName",
				(ApiRequest request) =>
				{
					var bestBookName = _bookSelection.CurrentSelection?.TitleBestForUserDisplay;
					request.ReplyWithText(bestBookName ?? "??");
				}, true);

			// ProblemDialog.tsx uses this endpoint to get the registered user's email address.
			apiHandler.RegisterEndpointHandler("problemReport/emailAddress",
				(ApiRequest request) =>
				{
					request.ReplyWithText(SIL.Windows.Forms.Registration.Registration.Default.Email);
				}, true);

			// PrivacyScreen.tsx uses this endpoint to show the user what info will be included in the report.
			apiHandler.RegisterEndpointHandler("problemReport/diagnosticInfo",
				(ApiRequest request) =>
				{
					var userWantsToIncludeBook = request.RequiredParam("includeBook") == "true";
					var userInput = request.RequiredParam("userInput");
					var userEmail = request.RequiredParam("email");
					request.ReplyWithText(GetDiagnosticInfo(userWantsToIncludeBook, userInput, userEmail));
				}, true);

			// ProblemDialog.tsx uses this endpoint in its AttemptSubmit method;
			// it expects a response that it will use to show the issue link to the user.
			apiHandler.RegisterEndpointHandler("problemReport/submit",
				(ApiRequest request) =>
				{
					var report = DynamicJson.Parse(request.RequiredPostJson());
					var subject = report.kind == "User" ? "User Problem" : report.kind == "Fatal" ? "Crash Report" : "Error Report";

					var issueSubmission = new YouTrackIssueSubmitter(YouTrackProjectKey);
					var userDesc = report.userInput as string;
					var userEmail = report.email as string;
					if (report.includeScreenshot && _screenshotTempFile != null && RobustFile.Exists(_screenshotTempFile.Path))
					{
						issueSubmission.AddAttachment(_screenshotTempFile?.Path);
					}
					if(report.includeBook)
					{
						try
						{
							_bookZipFile = new BloomZipFile(_bookZipFileTemp.Path);
							_bookZipFile.AddDirectory(_bookSelection.CurrentSelection.StoragePageFolder);
							if (WantReaderInfo(true))
							{
								AddReaderInfo();
							}
							AddCollectionSettings();
							_bookZipFile.Save();
							issueSubmission.AddAttachment(_bookZipFileTemp.Path);
						}
						catch (Exception error)
						{
							var msg = "***Error as ProblemReportApi attempted to zip up the book: " + error.Message;
							userDesc += Environment.NewLine + msg;
							Logger.WriteEvent(msg);
							DisposeOfZipRemnants(report.includeBook);
						}
					}
					var diagnosticInfo = GetDiagnosticInfo(report.includeBook, userDesc, userEmail);
					if (!string.IsNullOrWhiteSpace(userEmail))
					{
						// remember their email
						SIL.Windows.Forms.Registration.Registration.Default.Email = userEmail;
					}

					const string failureResult = "failed";
					string issueId;
					try
					{
						issueId = issueSubmission.SubmitToYouTrack(subject, diagnosticInfo);
					}
					catch (Exception e)
					{
						Debug.Fail("Submitting problem report to YouTrack failed with '" + e.Message + "'.");
						DisposeOfZipRemnants(report.includeBook);
						issueId = failureResult;
					}
					object linkToNewIssue;
					if (issueId == failureResult)
					{
						linkToNewIssue = new {issueLink = failureResult + ":" + _bookZipFileTemp.Path};
						_bookZipFileTemp.Detach(); // so it doesn't go away before the user can email it to us.
					}
					else
					{
						linkToNewIssue = new { issueLink = "https://issues.bloomlibrary.org/youtrack/issue/" + issueId};
					}
					request.ReplyWithJson(linkToNewIssue);
				}, true);
		}

		private void DisposeOfZipRemnants(bool includeBook)
		{
			// if an error happens in the zipper, the zip file stays locked, so we just leak it
			if (includeBook)
			{
				_bookZipFileTemp.Detach();
			}

			_bookZipFile = null;
		}

		private void AddReaderInfo()
		{
			var filePaths = GetReaderFilePaths(CollectionFolder);
			foreach (var filePath in filePaths)
			{
				_bookZipFile.AddTopLevelFile(filePath);
			}
		}

		private void AddCollectionSettings()
		{
			var filePaths = GetCollectionFilePaths(CollectionFolder);
			foreach (var filePath in filePaths)
			{
				_bookZipFile.AddTopLevelFile(filePath);
			}
		}

		/// <summary>
		/// Shows a problem dialog. Use to match <code>Action<Exception, string></code> signature
		/// in ErrorReport.OnShowDetails().
		/// </summary>
		/// <param name="exception"></param>
		/// <param name="detailedMessage"></param>
		public static void ShowProblemDialogForNonFatalException(Exception exception,
			string detailedMessage = "")
		{
			ShowProblemDialog(null, exception, detailedMessage, "nonfatal");
		}

		/// <summary>
		/// Shows a problem dialog.
		/// </summary>
		/// <param name="controlForScreenshotting"></param>
		/// <param name="exception"></param>
		/// <param name="detailedMessage"></param>
		/// <param name="levelOfProblem"></param>
		public static void ShowProblemDialog(Control controlForScreenshotting, Exception exception,
			string detailedMessage = "", string levelOfProblem="user")
		{
			_currentException = exception;
			_detailedMessage = detailedMessage;
			if (controlForScreenshotting == null)
				controlForScreenshotting = Form.ActiveForm;
			if (controlForScreenshotting == null) // still possible if we come from a "Details" button
				controlForScreenshotting = FatalExceptionHandler.ControlOnUIThread;
			SafeInvoke.InvokeIfPossible("Screen Shot", controlForScreenshotting, false,
				() =>
				{
					try
					{
						var bounds = controlForScreenshotting.Bounds;
						var screenshot = new Bitmap(bounds.Width, bounds.Height);
						using (var g = Graphics.FromImage(screenshot))
						{
							g.CopyFromScreen(controlForScreenshotting.PointToScreen(new Point(bounds.Left, bounds.Top)), Point.Empty,
								bounds.Size);
						}

						_screenshotTempFile = TempFile.WithFilename("screenshot.png");
						RobustImageIO.SaveImage(screenshot, _screenshotTempFile.Path, ImageFormat.Png);
					}
					catch (Exception e)
					{
						_screenshotTempFile = null;
						Logger.WriteError("Bloom was unable to create a screenshot.", e);
					}

					var query = "?" + levelOfProblem;
					var problemDialogRootPath = BloomFileLocator.GetBrowserFile(false,  "problemDialog", "loader.html");
					var url = problemDialogRootPath.ToLocalhost() + query;
					using (var dlg = new BrowserDialog(url))
					{
						dlg.ShowDialog();
					}
				});
		}

		private string GetDiagnosticInfo(bool includeBook, string userDescription, string userEmail)
		{
			var bldr = new StringBuilder();

			bldr.AppendLine("### Problem Description");
			bldr.AppendLine(userDescription);
			bldr.AppendLine();

			GetInformationAboutUser(bldr, userEmail);
			GetExceptionInformation(bldr);
			GetStandardErrorReportingProperties(bldr, true);
			GetAdditionalBloomEnvironmentInfo(bldr);
			GetAdditionalFileInfo(bldr, includeBook);
			return bldr.ToString();
		}

		private static void GetExceptionInformation(StringBuilder bldr)
		{
			if (_currentException == null && string.IsNullOrWhiteSpace(_detailedMessage))
				return;
			if (_currentException != null)
			{
				Exception dummy = null;
				bldr.AppendLine();
				bldr.AppendLine("#### Exception Details");
				if (!string.IsNullOrWhiteSpace(_detailedMessage))
				{
					bldr.AppendLine(_detailedMessage);
					bldr.AppendLine();
				}
				bldr.AppendLine("```stacktrace");
				bldr.Append(ExceptionHelper.GetHiearchicalExceptionInfo(_currentException, ref dummy));
				bldr.AppendLine("```");
			}
			else
			{
				// No exception, but we do have a detailed message from Bloom. This may not actually ever occur.
				bldr.AppendLine();
				bldr.AppendLine("#### Detailed message");
				bldr.AppendLine(_detailedMessage);
			}
		}

		private static string GetObfuscatedEmail(string userEmail = "")
		{
			var email = string.IsNullOrWhiteSpace(userEmail) ?
				SIL.Windows.Forms.Registration.Registration.Default.Email :
				userEmail;
			string obfuscatedEmail;
			try
			{
				var m = new MailAddress(email);
				// note: we have code in YouTrack that de-obfuscates this particular format, so don't mess with it
				obfuscatedEmail = string.Format("{1} {0}", m.User, m.Host).Replace(".", "/");
			}
			catch (Exception)
			{
				obfuscatedEmail = email; // ah well, it's not valid anyhow, so no need to obfuscate (other code may not let the user get this far anyhow)
			}
			return obfuscatedEmail;
		}

		private static void GetInformationAboutUser(StringBuilder bldr, string userEmail)
		{
			var firstName = SIL.Windows.Forms.Registration.Registration.Default.FirstName;
			var lastName = SIL.Windows.Forms.Registration.Registration.Default.Surname;
			var nameString = GetNameString(firstName, lastName);
			var obfuscatedEmail = GetObfuscatedEmail(userEmail);
			var emailString = string.IsNullOrWhiteSpace(obfuscatedEmail) ? string.Empty : " (" + obfuscatedEmail + ")";
			bldr.AppendLine("Error Report from " + nameString + emailString + " on " + DateTime.UtcNow.ToUniversalTime() + " UTC");
		}

		private static object GetNameString(string firstName, string lastName)
		{
			return !string.IsNullOrWhiteSpace(firstName) && !string.IsNullOrWhiteSpace(lastName)
				? lastName + ", " + firstName
				: string.IsNullOrWhiteSpace(lastName) && string.IsNullOrWhiteSpace(firstName) ?
					"unknown" :
					(lastName + firstName).Trim();
		}


		private static void GetStandardErrorReportingProperties(StringBuilder bldr, bool appendLog)
		{
			const string Version = "Version";
			bldr.AppendLine("#### Error Reporting Properties");
			foreach (string label in ErrorReport.Properties.Keys)
			{
				if (label == Version)
					bldr.Append("**"); // Version is the most important of these; bring it out a bit
				bldr.Append(label);
				bldr.Append(": ");
				bldr.Append(ErrorReport.Properties[label]);
				bldr.AppendLine(label == Version ? "**" : "");
			}

			if (appendLog || Logger.Singleton == null)
			{
				bldr.AppendLine();
				bldr.AppendLine("#### Log");
				bldr.AppendLine("```stacktrace");
				bldr.AppendLine();
				try
				{
					bldr.Append(Logger.LogText);
				}
				catch (Exception err)
				{
					// We have more than one report of dying while logging an exception.
					bldr.AppendLine("****Could not read from log: " + err.Message);
				}
				bldr.AppendLine("```");
			}
		}

		private void GetAdditionalBloomEnvironmentInfo(StringBuilder bldr)
		{
			var book = _bookSelection.CurrentSelection;
			var projectName = book?.CollectionSettings.CollectionName;
			bldr.AppendLine("#### Additional User Environment Information");
			if (book == null)
			{
				bldr.AppendLine("No Book was selected.");
				return;
			}
			try
			{
				bldr.AppendLine("Collection name: " + projectName);
				var sizeOrient = book.GetLayout().SizeAndOrientation;
				bldr.AppendLine("Page Size/Orientation: " + sizeOrient);
			}
			catch (Exception)
			{
				bldr.AppendLine("GetLayout() or SizeAndOrientation threw an exception.");
			}
			var settings = book.CollectionSettings;
			if (settings == null)
			{
				// paranoia, shouldn't happen
				bldr.AppendLine("Book's CollectionSettings was null.");
				return;
			}
			bldr.AppendLine("Collection name: " + settings.CollectionName);
			bldr.AppendLine("xMatter pack name: " + settings.XMatterPackName);
			bldr.AppendLine("Language1 -> iso: '" + settings.Language1Iso639Code + "',  font: " +
							settings.Language1.FontName + (settings.Language1.IsRightToLeft ? " RTL" : string.Empty));
			bldr.AppendLine("Language2 -> iso: '" + settings.Language2Iso639Code + "',  font: " +
							settings.Language2.FontName + (settings.Language2.IsRightToLeft ? " RTL" : string.Empty));
			if (string.IsNullOrEmpty(settings.Language3Iso639Code))
			{
				bldr.AppendLine("No Language3 defined");
			}
			else
			{
				bldr.AppendLine("Language3 -> iso: '" + settings.Language3Iso639Code + "',  font: " +
					settings.Language3.FontName + (settings.Language3.IsRightToLeft ? " RTL" : string.Empty));
			}
		}

		private void GetAdditionalFileInfo(StringBuilder bldr, bool includeBook)
		{
			var book = _bookSelection.CurrentSelection;
			if (string.IsNullOrEmpty(book?.FolderPath))
				return;
			bldr.AppendLine();
			bldr.AppendLine("#### Additional Files Bundled With Book");
			var collectionFolder = Path.GetDirectoryName(book.FolderPath);
			if (collectionFolder == null)
				return; // mostly to avoid blue squiggles in VS
			if (WantReaderInfo(includeBook))
			{
				var listOfReaderFiles = GetReaderFilePaths(collectionFolder);
				ListFiles(listOfReaderFiles, bldr);
			}
			var listOfCollectionFiles = GetCollectionFilePaths(collectionFolder);
			ListFiles(listOfCollectionFiles, bldr);
		}

		private static IEnumerable<string> GetReaderFilePaths(string collectionFolder)
		{
			var result = Directory.GetFiles(collectionFolder, "ReaderTools*-*.json").ToList();
			ListFolderContents(Path.Combine(collectionFolder, "Allowed Words"), result);
			ListFolderContents(Path.Combine(collectionFolder, "Sample Texts"), result);
			return result;
		}

		private static IEnumerable<string> GetCollectionFilePaths(string collectionFolder)
		{
			var result = Directory.GetFiles(collectionFolder, "*CollectionStyles.css").ToList();
			result.AddRange(Directory.GetFiles(collectionFolder, "*.bloomCollection"));
			return result;
		}

		private static void ListFiles(IEnumerable<string> filePaths, StringBuilder bldr)
		{
			foreach (var filePath in filePaths)
				bldr.AppendLine(filePath);
		}

		private bool WantReaderInfo(bool includeBook)
		{
			var book = _bookSelection.CurrentSelection;

			if (book == null || !includeBook)
				return false;
			foreach (var tool in book.BookInfo.Tools)
			{
				if (tool.ToolId == "decodableReader" || tool.ToolId == "leveledReader")
					return true;
			}
			return false;
		}

		private static void ListFolderContents(string folder, List<string> listOfFilePaths)
		{
			if (!Directory.Exists(folder))
				return;
			listOfFilePaths.AddRange(Directory.GetFiles(folder));
			// Probably overkill, but if there are subfolders, they will be zipped up with the book.
			foreach (var sub in Directory.GetDirectories(folder))
				ListFolderContents(sub, listOfFilePaths);
		}

	
		public void Dispose()
		{
			_screenshotTempFile?.Dispose();
			_bookZipFile = null;
			_bookZipFileTemp?.Dispose();
		}
	}
}