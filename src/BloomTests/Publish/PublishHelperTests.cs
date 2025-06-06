﻿using System;
using System.Collections.Generic;
using Bloom.Publish;
using Bloom.SafeXml;
using NUnit.Framework;

namespace BloomTests.Publish
{
    /// <summary>
    /// This class is used to test internals of the PublishHelper class.
    /// </summary>
    internal class TestPublishHelper : PublishHelper
    {
        internal Dictionary<string, string> MapIdToDisplay => this._mapIdToDisplay;
        internal Dictionary<string, FontInfo> MapIdToFontInfo => this._mapIdToFontInfo;

        internal void TestStoreFontUsed(SafeXmlElement elt)
        {
            StoreFontUsed(elt);
        }

        internal bool TestIsRealTextDisplayedInDesiredFont(
            SafeXmlElement element,
            FontInfo desiredFont
        )
        {
            return IsRealTextDisplayedInDesiredFont(element, desiredFont);
        }
    }

    [TestFixture]
    public class PublishHelperTests
    {
        private TestPublishHelper _helper;

        [OneTimeSetUp]
        public void Setup()
        {
            _helper = new TestPublishHelper();
            InitializeHelperMaps();
        }

        [OneTimeTearDown]
        public void TearDown()
        {
            _helper.Dispose();
            _helper = null;
        }

        [TestCase("width", "width: 1433.16px; top: -146.703px; left: -706.162px;", 1433.16)]
        [TestCase("top", "width: 1433.16px; top: -146.703px; left: -706.162px;", -146.703)]
        [TestCase("left", "width: 1433.16px; top: -146.703px; left: -706.162px;", -706.162)]
        [TestCase("left", "some silly nonsence", 0)]
        [TestCase("left", "width: 20", 0)]
        public void GetNumberFromPx(string label, string input, double expected)
        {
            Assert.That(
                Math.Abs(expected - PublishHelper.GetNumberFromPx(label, input)),
                Is.LessThan(0.001)
            );
        }

        private string _htmlCoverPage =
            @"
<div class=""bloom-page cover coverColor bloom-frontMatter frontCover outsideFrontCover side-right Device16x9Landscape"" data-page=""required singleton"" data-export=""front-matter-cover"" data-xmatter-page=""frontCover"" id=""adff8e4d-d01d-45bb-8325-9b5e28f980a5"" lang=""tpi"" data-page-number="""">
  <div class=""marginBox"" id=""PublishTempIdXXYY1"">
    <div class=""bloom-translationGroup bookTitle"" data-default-languages=""V,N1"" data-visibility-variable=""cover-title-LN-show"" id=""PublishTempIdXXYY3"">
      <div class=""bloom-editable"" lang=""z"" data-book=""bookTitle"" id=""PublishTempIdXXYY4"" />
      <div class=""bloom-editablebloom-visibility-code-on bloom-content1 bloom-contentFirst "" lang=""en"" data-book=""bookTitle"" style=""padding-bottom: 0px;"" id=""PublishTempIdXXYY5"">
        <p>My New Car</p>
      </div>
      <div class=""bloom-editable bloom-visibility-code-on bloom-contentNational1 bloom-contentSecond"" lang=""tpi"" data-book=""bookTitle"" id=""PublishTempIdXXYY6"">
        <p>Nupela kar bilong mi</p>
      </div>
      <div class=""bloom-editable bloom-contentNational2"" lang=""gaj"" data-book=""bookTitle"" id=""PublishTempIdXXYY7"" />
    </div>
  </div>
</div>
";

        [Test]
        public void FrontCoverPage_FontsUsed()
        {
            _helper.FontsUsed.Clear();
            var doc = SafeXmlDocument.Create();
            doc.LoadXml(_htmlCoverPage);
            foreach (SafeXmlElement elt in doc.SafeSelectNodes(".//div[@id]"))
            {
                _helper.TestStoreFontUsed(elt);
            }
            Assert.That(_helper.FontsUsed.Count, Is.EqualTo(2));
            var firstFont = new PublishHelper.FontInfo
            {
                fontFamily = "ABeeZee",
                fontStyle = "normal",
                fontWeight = "700"
            };
            var secondFont = new PublishHelper.FontInfo
            {
                fontFamily = "Aileron",
                fontStyle = "normal",
                fontWeight = "700"
            };
            Assert.That(_helper.FontsUsed.Contains(firstFont), Is.True);
            Assert.That(_helper.FontsUsed.Contains(secondFont), Is.True);
        }

        [Test]
        public void FrontCoverPage_IsRealTextDisplayedInDesiredFont()
        {
            _helper.FontsUsed.Clear();
            var doc = SafeXmlDocument.Create();
            doc.LoadXml(_htmlCoverPage);
            Assert.That(
                _helper.TestIsRealTextDisplayedInDesiredFont(
                    doc.DocumentElement,
                    new PublishHelper.FontInfo
                    {
                        fontFamily = "ABeeZee",
                        fontStyle = "normal",
                        fontWeight = "400"
                    }
                ),
                Is.False
            );
            Assert.That(
                _helper.TestIsRealTextDisplayedInDesiredFont(
                    doc.DocumentElement,
                    new PublishHelper.FontInfo
                    {
                        fontFamily = "ABeeZee",
                        fontStyle = "normal",
                        fontWeight = "700"
                    }
                ),
                Is.True
            );
            Assert.That(
                _helper.TestIsRealTextDisplayedInDesiredFont(
                    doc.DocumentElement,
                    new PublishHelper.FontInfo
                    {
                        fontFamily = "ABeeZee",
                        fontStyle = "italic",
                        fontWeight = "400"
                    }
                ),
                Is.False
            );
            Assert.That(
                _helper.TestIsRealTextDisplayedInDesiredFont(
                    doc.DocumentElement,
                    new PublishHelper.FontInfo
                    {
                        fontFamily = "ABeeZee",
                        fontStyle = "italic",
                        fontWeight = "700"
                    }
                ),
                Is.False
            );
            Assert.That(
                _helper.TestIsRealTextDisplayedInDesiredFont(
                    doc.DocumentElement,
                    new PublishHelper.FontInfo
                    {
                        fontFamily = "Aileron",
                        fontStyle = "normal",
                        fontWeight = "400"
                    }
                ),
                Is.False
            );
            Assert.That(
                _helper.TestIsRealTextDisplayedInDesiredFont(
                    doc.DocumentElement,
                    new PublishHelper.FontInfo
                    {
                        fontFamily = "Aileron",
                        fontStyle = "normal",
                        fontWeight = "700"
                    }
                ),
                Is.True
            );
            Assert.That(
                _helper.TestIsRealTextDisplayedInDesiredFont(
                    doc.DocumentElement,
                    new PublishHelper.FontInfo
                    {
                        fontFamily = "Aileron",
                        fontStyle = "italic",
                        fontWeight = "400"
                    }
                ),
                Is.False
            );
            Assert.That(
                _helper.TestIsRealTextDisplayedInDesiredFont(
                    doc.DocumentElement,
                    new PublishHelper.FontInfo
                    {
                        fontFamily = "Aileron",
                        fontStyle = "italic",
                        fontWeight = "700"
                    }
                ),
                Is.False
            );
            Assert.That(
                _helper.TestIsRealTextDisplayedInDesiredFont(
                    doc.DocumentElement,
                    new PublishHelper.FontInfo
                    {
                        fontFamily = "Andika",
                        fontStyle = "normal",
                        fontWeight = "400"
                    }
                ),
                Is.False
            );
            Assert.That(
                _helper.TestIsRealTextDisplayedInDesiredFont(
                    doc.DocumentElement,
                    new PublishHelper.FontInfo
                    {
                        fontFamily = "Andika",
                        fontStyle = "normal",
                        fontWeight = "700"
                    }
                ),
                Is.False
            );
            Assert.That(
                _helper.TestIsRealTextDisplayedInDesiredFont(
                    doc.DocumentElement,
                    new PublishHelper.FontInfo
                    {
                        fontFamily = "Andika",
                        fontStyle = "italic",
                        fontWeight = "400"
                    }
                ),
                Is.False
            );
            Assert.That(
                _helper.TestIsRealTextDisplayedInDesiredFont(
                    doc.DocumentElement,
                    new PublishHelper.FontInfo
                    {
                        fontFamily = "Andika",
                        fontStyle = "italic",
                        fontWeight = "700"
                    }
                ),
                Is.False
            );
        }

        private string _htmlNumberedPage =
            @"<div class=""bloom-page numberedPage"" id=""40fc8170-b82b-48ec-9ce4-b5be38d4836c"" data-page-number=""1"" lang="""">
  <div class=""marginBox"" id=""PublishTempIdXXYY29"">
    <div class=""bloom-translationGroup bloom-trailingElement bloom-vertical-align-center"" data-default-languages=""auto"" style=""font-size: 16px;"" id=""PublishTempIdXXYY30"">
      <div class=""bloom-editable normal-style"" lang=""es"" style="""" id=""PublishTempIdXXYY31"">
        <p>Recibí un auto nuevo para mi cumpleaños.</p>
      </div>
      <div class=""bloom-editable normal-style bloom-visibility-code-on bloom-content1"" lang=""en"" style=""min-height: 24px;"" tabindex=""0"" data-languagetipcontent=""English"" id=""PublishTempIdXXYY32"">
        <p>I got a new car for my birthday</p>
      </div>
      <div class=""bloom-editable normal-style bloom-visibility-code-on bloom-content2"" lang=""tpi"" style=""min-height: 24px;"" tabindex=""0"" data-languagetipcontent=""English"" id=""PublishTempIdXXYY33"">
        <p>Mi kisim nupela kar long betde bilong mi</p>
      </div>
    </div>
  </div>
</div>
";

        [Test]
        public void NumberedPage_FontsUsed()
        {
            _helper.FontsUsed.Clear();
            var doc = SafeXmlDocument.Create();
            doc.LoadXml(_htmlNumberedPage);
            foreach (SafeXmlElement elt in doc.SafeSelectNodes(".//div[@id]"))
            {
                _helper.TestStoreFontUsed(elt);
            }
            Assert.That(_helper.FontsUsed.Count, Is.EqualTo(3));
            var firstFont = new PublishHelper.FontInfo
            {
                fontFamily = "ABeeZee",
                fontStyle = "normal",
                fontWeight = "400"
            };
            var secondFont = new PublishHelper.FontInfo
            {
                fontFamily = "Aileron",
                fontStyle = "normal",
                fontWeight = "400"
            };
            var thirdFont = new PublishHelper.FontInfo
            {
                fontFamily = "Andika",
                fontStyle = "italic",
                fontWeight = "400"
            };
            var fourthFont = new PublishHelper.FontInfo
            {
                fontFamily = "Andika",
                fontStyle = "normal",
                fontWeight = "400"
            };
            Assert.That(_helper.FontsUsed.Contains(firstFont), Is.True);
            Assert.That(_helper.FontsUsed.Contains(secondFont), Is.True);
            Assert.That(_helper.FontsUsed.Contains(thirdFont), Is.True);
            Assert.That(_helper.FontsUsed.Contains(fourthFont), Is.False);
        }

        [Test]
        public void NumberedPage_IsRealTextDisplayedInDesiredFont()
        {
            _helper.FontsUsed.Clear();
            var doc = SafeXmlDocument.Create();
            doc.LoadXml(_htmlNumberedPage);
            Assert.That(
                _helper.TestIsRealTextDisplayedInDesiredFont(
                    doc.DocumentElement,
                    new PublishHelper.FontInfo
                    {
                        fontFamily = "ABeeZee",
                        fontStyle = "normal",
                        fontWeight = "400"
                    }
                ),
                Is.True
            );
            Assert.That(
                _helper.TestIsRealTextDisplayedInDesiredFont(
                    doc.DocumentElement,
                    new PublishHelper.FontInfo
                    {
                        fontFamily = "ABeeZee",
                        fontStyle = "normal",
                        fontWeight = "700"
                    }
                ),
                Is.False
            );
            Assert.That(
                _helper.TestIsRealTextDisplayedInDesiredFont(
                    doc.DocumentElement,
                    new PublishHelper.FontInfo
                    {
                        fontFamily = "ABeeZee",
                        fontStyle = "italic",
                        fontWeight = "400"
                    }
                ),
                Is.False
            );
            Assert.That(
                _helper.TestIsRealTextDisplayedInDesiredFont(
                    doc.DocumentElement,
                    new PublishHelper.FontInfo
                    {
                        fontFamily = "ABeeZee",
                        fontStyle = "italic",
                        fontWeight = "700"
                    }
                ),
                Is.False
            );
            Assert.That(
                _helper.TestIsRealTextDisplayedInDesiredFont(
                    doc.DocumentElement,
                    new PublishHelper.FontInfo
                    {
                        fontFamily = "Aileron",
                        fontStyle = "normal",
                        fontWeight = "400"
                    }
                ),
                Is.True
            );
            Assert.That(
                _helper.TestIsRealTextDisplayedInDesiredFont(
                    doc.DocumentElement,
                    new PublishHelper.FontInfo
                    {
                        fontFamily = "Aileron",
                        fontStyle = "normal",
                        fontWeight = "700"
                    }
                ),
                Is.False
            );
            Assert.That(
                _helper.TestIsRealTextDisplayedInDesiredFont(
                    doc.DocumentElement,
                    new PublishHelper.FontInfo
                    {
                        fontFamily = "Aileron",
                        fontStyle = "italic",
                        fontWeight = "400"
                    }
                ),
                Is.False
            );
            Assert.That(
                _helper.TestIsRealTextDisplayedInDesiredFont(
                    doc.DocumentElement,
                    new PublishHelper.FontInfo
                    {
                        fontFamily = "Aileron",
                        fontStyle = "italic",
                        fontWeight = "700"
                    }
                ),
                Is.False
            );
            Assert.That(
                _helper.TestIsRealTextDisplayedInDesiredFont(
                    doc.DocumentElement,
                    new PublishHelper.FontInfo
                    {
                        fontFamily = "Andika",
                        fontStyle = "normal",
                        fontWeight = "400"
                    }
                ),
                Is.False
            );
            Assert.That(
                _helper.TestIsRealTextDisplayedInDesiredFont(
                    doc.DocumentElement,
                    new PublishHelper.FontInfo
                    {
                        fontFamily = "Andika",
                        fontStyle = "normal",
                        fontWeight = "700"
                    }
                ),
                Is.False
            );
            Assert.That(
                _helper.TestIsRealTextDisplayedInDesiredFont(
                    doc.DocumentElement,
                    new PublishHelper.FontInfo
                    {
                        fontFamily = "Andika",
                        fontStyle = "italic",
                        fontWeight = "400"
                    }
                ),
                Is.True
            );
            Assert.That(
                _helper.TestIsRealTextDisplayedInDesiredFont(
                    doc.DocumentElement,
                    new PublishHelper.FontInfo
                    {
                        fontFamily = "Andika",
                        fontStyle = "italic",
                        fontWeight = "700"
                    }
                ),
                Is.False
            );
        }

        private string _htmlPageWithDisplayedEmptyText =
            @"<div class=""bloom-page numberedPage"" id=""40fc8170-b82b-48ec-9ce4-b5be38d4836c"" data-page-number=""1"" lang="""">
  <div class=""marginBox"" id=""PublishTempIdXXYY29"">
    <div class=""bloom-translationGroup bloom-trailingElement bloom-vertical-align-center"" data-default-languages=""auto"" style=""font-size: 16px;"" id=""PublishTempIdXXYY30"">
      <div class=""bloom-editable normal-style"" lang=""es"" style="""" id=""PublishTempIdXXYY31"">
        <p></p>
      </div>
      <div class=""bloom-editable normal-style bloom-visibility-code-on bloom-content1"" lang=""en"" style=""min-height: 24px;"" tabindex=""0"" data-languagetipcontent=""English"" id=""PublishTempIdXXYY32"">
        <p></p>
      </div>
      <div class=""bloom-editable normal-style bloom-visibility-code-on bloom-content2"" lang=""tpi"" style=""min-height: 24px;"" tabindex=""0"" data-languagetipcontent=""English"" id=""PublishTempIdXXYY33"">
        <p></p>
      </div>
    </div>
  </div>
</div>
";

        private string _htmlPageWithHiddenEmptyText =
            @"<div class=""bloom-page numberedPage"" id=""3e9952c0-0d64-4812-bca3-145fe70b62a7"" data-page-number=""1"" lang="""">
  <div class=""marginBox"" id=""PublishTempIdXXYY41"">
    <div class=""bloom-translationGroup bloom-trailingElement bloom-vertical-align-center"" data-default-languages=""auto"" style=""font-size: 16px;"" id=""PublishTempIdXXYY42"">
      <div class=""bloom-editable normal-style"" lang=""es"" style="""" id=""PublishTempIdXXYY43"">
        <p></p>
      </div>
      <div class=""bloom-editable normal-style bloom-visibility-code-on bloom-content1"" lang=""en"" style=""min-height: 24px;"" tabindex=""0"" data-languagetipcontent=""English"" id=""PublishTempIdXXYY44"">
        <p></p>
      </div>
      <div class=""bloom-editable normal-style bloom-visibility-code-on bloom-content2"" lang=""tpi"" style=""min-height: 24px;"" tabindex=""0"" data-languagetipcontent=""English"" id=""PublishTempIdXXYY45"">
        <p></p>
      </div>
    </div>
  </div>
</div>
";

        [Test]
        public void PageWithDisplayedEmptyText_FontsUsed()
        {
            _helper.FontsUsed.Clear();
            var doc = SafeXmlDocument.Create();
            doc.LoadXml(_htmlPageWithDisplayedEmptyText);
            foreach (SafeXmlElement elt in doc.SafeSelectNodes(".//div[@id]"))
            {
                _helper.TestStoreFontUsed(elt);
            }
            // This may be surprising, but empty text elements that are displayed cause the font
            // information to be stored.
            Assert.That(_helper.FontsUsed.Count, Is.EqualTo(3));
        }

        [Test]
        public void PageWithHiddenEmptyText_FontsUsed()
        {
            _helper.FontsUsed.Clear();
            var doc = SafeXmlDocument.Create();
            doc.LoadXml(_htmlPageWithHiddenEmptyText);
            foreach (SafeXmlElement elt in doc.SafeSelectNodes(".//div[@id]"))
            {
                _helper.TestStoreFontUsed(elt);
            }
            // If empty text is not displayed, the font information is not stored.
            Assert.That(_helper.FontsUsed.Count, Is.EqualTo(0));
        }

        [TestCase("displayed")]
        [TestCase("hidden")]
        public void PageWithEmptyText_IsRealTextDisplayedInDesiredFont(string type)
        {
            _helper.FontsUsed.Clear();
            var doc = SafeXmlDocument.Create();
            // Whether it is displayed or not, IsRealTextDisplayedInDesiredFont should
            // return false for empty text.
            switch (type)
            {
                case "displayed":
                    doc.LoadXml(_htmlPageWithDisplayedEmptyText);
                    break;
                case "hidden":
                    doc.LoadXml(_htmlPageWithHiddenEmptyText);
                    break;
            }
            Assert.That(
                _helper.TestIsRealTextDisplayedInDesiredFont(
                    doc.DocumentElement,
                    new PublishHelper.FontInfo
                    {
                        fontFamily = "ABeeZee",
                        fontStyle = "normal",
                        fontWeight = "400"
                    }
                ),
                Is.False
            );
            Assert.That(
                _helper.TestIsRealTextDisplayedInDesiredFont(
                    doc.DocumentElement,
                    new PublishHelper.FontInfo
                    {
                        fontFamily = "ABeeZee",
                        fontStyle = "normal",
                        fontWeight = "700"
                    }
                ),
                Is.False
            );
            Assert.That(
                _helper.TestIsRealTextDisplayedInDesiredFont(
                    doc.DocumentElement,
                    new PublishHelper.FontInfo
                    {
                        fontFamily = "ABeeZee",
                        fontStyle = "italic",
                        fontWeight = "400"
                    }
                ),
                Is.False
            );
            Assert.That(
                _helper.TestIsRealTextDisplayedInDesiredFont(
                    doc.DocumentElement,
                    new PublishHelper.FontInfo
                    {
                        fontFamily = "ABeeZee",
                        fontStyle = "italic",
                        fontWeight = "700"
                    }
                ),
                Is.False
            );
            Assert.That(
                _helper.TestIsRealTextDisplayedInDesiredFont(
                    doc.DocumentElement,
                    new PublishHelper.FontInfo
                    {
                        fontFamily = "Aileron",
                        fontStyle = "normal",
                        fontWeight = "400"
                    }
                ),
                Is.False
            );
            Assert.That(
                _helper.TestIsRealTextDisplayedInDesiredFont(
                    doc.DocumentElement,
                    new PublishHelper.FontInfo
                    {
                        fontFamily = "Aileron",
                        fontStyle = "normal",
                        fontWeight = "700"
                    }
                ),
                Is.False
            );
            Assert.That(
                _helper.TestIsRealTextDisplayedInDesiredFont(
                    doc.DocumentElement,
                    new PublishHelper.FontInfo
                    {
                        fontFamily = "Aileron",
                        fontStyle = "italic",
                        fontWeight = "400"
                    }
                ),
                Is.False
            );
            Assert.That(
                _helper.TestIsRealTextDisplayedInDesiredFont(
                    doc.DocumentElement,
                    new PublishHelper.FontInfo
                    {
                        fontFamily = "Aileron",
                        fontStyle = "italic",
                        fontWeight = "700"
                    }
                ),
                Is.False
            );
            Assert.That(
                _helper.TestIsRealTextDisplayedInDesiredFont(
                    doc.DocumentElement,
                    new PublishHelper.FontInfo
                    {
                        fontFamily = "Andika",
                        fontStyle = "normal",
                        fontWeight = "400"
                    }
                ),
                Is.False
            );
            Assert.That(
                _helper.TestIsRealTextDisplayedInDesiredFont(
                    doc.DocumentElement,
                    new PublishHelper.FontInfo
                    {
                        fontFamily = "Andika",
                        fontStyle = "normal",
                        fontWeight = "700"
                    }
                ),
                Is.False
            );
            Assert.That(
                _helper.TestIsRealTextDisplayedInDesiredFont(
                    doc.DocumentElement,
                    new PublishHelper.FontInfo
                    {
                        fontFamily = "Andika",
                        fontStyle = "italic",
                        fontWeight = "400"
                    }
                ),
                Is.False
            );
            Assert.That(
                _helper.TestIsRealTextDisplayedInDesiredFont(
                    doc.DocumentElement,
                    new PublishHelper.FontInfo
                    {
                        fontFamily = "Andika",
                        fontStyle = "italic",
                        fontWeight = "700"
                    }
                ),
                Is.False
            );
        }

        private void InitializeHelperMaps()
        {
            _helper.MapIdToDisplay.Clear();
            _helper.MapIdToFontInfo.Clear();
            // cover page font and display data
            _helper.MapIdToDisplay.Add("adff8e4d-d01d-45bb-8325-9b5e28f980a5", "block");
            _helper.MapIdToDisplay.Add("PublishTempIdXXYY1", "flex");
            _helper.MapIdToDisplay.Add("PublishTempIdXXYY3", "flex");
            _helper.MapIdToDisplay.Add("PublishTempIdXXYY4", "none");
            _helper.MapIdToDisplay.Add("PublishTempIdXXYY5", "block");
            _helper.MapIdToDisplay.Add("PublishTempIdXXYY6", "block");
            _helper.MapIdToDisplay.Add("PublishTempIdXXYY7", "none");
            _helper.MapIdToFontInfo.Add(
                "adff8e4d-d01d-45bb-8325-9b5e28f980a5",
                new PublishHelper.FontInfo
                {
                    fontFamily =
                        "Andika, \"Andika New Basic\", \"Andika Basic\", \"Gentium Basic\", \"Gentium Book Basic\", \"Doulos SIL\", sans-serif'",
                    fontStyle = "normal",
                    fontWeight = "400"
                }
            );
            _helper.MapIdToFontInfo.Add(
                "PublishTempIdXXYY1",
                new PublishHelper.FontInfo
                {
                    fontFamily =
                        "Andika, \"Andika New Basic\", \"Andika Basic\", \"Gentium Basic\", \"Gentium Book Basic\", \"Doulos SIL\", sans-serif'",
                    fontStyle = "normal",
                    fontWeight = "400"
                }
            );
            _helper.MapIdToFontInfo.Add(
                "PublishTempIdXXYY3",
                new PublishHelper.FontInfo
                {
                    fontFamily =
                        "Andika, \"Andika New Basic\", \"Andika Basic\", \"Gentium Basic\", \"Gentium Book Basic\", \"Doulos SIL\", sans-serif'",
                    fontStyle = "normal",
                    fontWeight = "400"
                }
            );
            _helper.MapIdToFontInfo.Add(
                "PublishTempIdXXYY4",
                new PublishHelper.FontInfo
                {
                    fontFamily = "ABeeZee",
                    fontStyle = "normal",
                    fontWeight = "700"
                }
            );
            _helper.MapIdToFontInfo.Add(
                "PublishTempIdXXYY5",
                new PublishHelper.FontInfo
                {
                    fontFamily = "ABeeZee",
                    fontStyle = "normal",
                    fontWeight = "700"
                }
            );
            _helper.MapIdToFontInfo.Add(
                "PublishTempIdXXYY6",
                new PublishHelper.FontInfo
                {
                    fontFamily = "Aileron",
                    fontStyle = "normal",
                    fontWeight = "700"
                }
            );
            _helper.MapIdToFontInfo.Add(
                "PublishTempIdXXYY7",
                new PublishHelper.FontInfo
                {
                    fontFamily = "Andika",
                    fontStyle = "normal",
                    fontWeight = "700"
                }
            );

            _helper.MapIdToDisplay.Add("40fc8170-b82b-48ec-9ce4-b5be38d4836c", "block");
            _helper.MapIdToDisplay.Add("PublishTempIdXXYY29", "block");
            _helper.MapIdToDisplay.Add("PublishTempIdXXYY30", "block");
            _helper.MapIdToDisplay.Add("PublishTempIdXXYY31", "block");
            _helper.MapIdToDisplay.Add("PublishTempIdXXYY32", "block");
            _helper.MapIdToDisplay.Add("PublishTempIdXXYY33", "block");
            _helper.MapIdToFontInfo.Add(
                "40fc8170-b82b-48ec-9ce4-b5be38d4836c",
                new PublishHelper.FontInfo
                {
                    fontFamily =
                        "Andika, \"Andika New Basic\", \"Andika Basic\", \"Gentium Basic\", \"Gentium Book Basic\", \"Doulos SIL\", sans-serif'",
                    fontStyle = "normal",
                    fontWeight = "400"
                }
            );
            _helper.MapIdToFontInfo.Add(
                "PublishTempIdXXYY29",
                new PublishHelper.FontInfo
                {
                    fontFamily =
                        "Andika, \"Andika New Basic\", \"Andika Basic\", \"Gentium Basic\", \"Gentium Book Basic\", \"Doulos SIL\", sans-serif'",
                    fontStyle = "normal",
                    fontWeight = "400"
                }
            );
            _helper.MapIdToFontInfo.Add(
                "PublishTempIdXXYY30",
                new PublishHelper.FontInfo
                {
                    fontFamily =
                        "Andika, \"Andika New Basic\", \"Andika Basic\", \"Gentium Basic\", \"Gentium Book Basic\", \"Doulos SIL\", sans-serif'",
                    fontStyle = "normal",
                    fontWeight = "400"
                }
            );
            _helper.MapIdToFontInfo.Add(
                "PublishTempIdXXYY31",
                new PublishHelper.FontInfo
                {
                    fontFamily = "Andika",
                    fontStyle = "italic",
                    fontWeight = "400"
                }
            );
            _helper.MapIdToFontInfo.Add(
                "PublishTempIdXXYY32",
                new PublishHelper.FontInfo
                {
                    fontFamily = "ABeeZee",
                    fontStyle = "normal",
                    fontWeight = "400"
                }
            );
            _helper.MapIdToFontInfo.Add(
                "PublishTempIdXXYY33",
                new PublishHelper.FontInfo
                {
                    fontFamily = "Aileron",
                    fontStyle = "normal",
                    fontWeight = "400"
                }
            );

            _helper.MapIdToDisplay.Add("3e9952c0-0d64-4812-bca3-145fe70b62a7", "block");
            _helper.MapIdToDisplay.Add("PublishTempIdXXYY41", "block");
            _helper.MapIdToDisplay.Add("PublishTempIdXXYY42", "block");
            _helper.MapIdToDisplay.Add("PublishTempIdXXYY43", "none");
            _helper.MapIdToDisplay.Add("PublishTempIdXXYY44", "none");
            _helper.MapIdToDisplay.Add("PublishTempIdXXYY45", "none");
            _helper.MapIdToFontInfo.Add(
                "3e9952c0-0d64-4812-bca3-145fe70b62a7",
                new PublishHelper.FontInfo
                {
                    fontFamily =
                        "Andika, \"Andika New Basic\", \"Andika Basic\", \"Gentium Basic\", \"Gentium Book Basic\", \"Doulos SIL\", sans-serif'",
                    fontStyle = "normal",
                    fontWeight = "400"
                }
            );
            _helper.MapIdToFontInfo.Add(
                "PublishTempIdXXYY41",
                new PublishHelper.FontInfo
                {
                    fontFamily =
                        "Andika, \"Andika New Basic\", \"Andika Basic\", \"Gentium Basic\", \"Gentium Book Basic\", \"Doulos SIL\", sans-serif'",
                    fontStyle = "normal",
                    fontWeight = "400"
                }
            );
            _helper.MapIdToFontInfo.Add(
                "PublishTempIdXXYY42",
                new PublishHelper.FontInfo
                {
                    fontFamily =
                        "Andika, \"Andika New Basic\", \"Andika Basic\", \"Gentium Basic\", \"Gentium Book Basic\", \"Doulos SIL\", sans-serif'",
                    fontStyle = "normal",
                    fontWeight = "400"
                }
            );
            _helper.MapIdToFontInfo.Add(
                "PublishTempIdXXYY43",
                new PublishHelper.FontInfo
                {
                    fontFamily = "Andika",
                    fontStyle = "normal",
                    fontWeight = "400"
                }
            );
            _helper.MapIdToFontInfo.Add(
                "PublishTempIdXXYY44",
                new PublishHelper.FontInfo
                {
                    fontFamily = "ABeeZee",
                    fontStyle = "normal",
                    fontWeight = "400"
                }
            );
            _helper.MapIdToFontInfo.Add(
                "PublishTempIdXXYY45",
                new PublishHelper.FontInfo
                {
                    fontFamily = "Aileron",
                    fontStyle = "normal",
                    fontWeight = "400"
                }
            );
        }
    }
}
