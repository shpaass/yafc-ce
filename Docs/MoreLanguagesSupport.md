# YAFC support for more languages

You can ask Yafc to display non-English text from the Welcome screen:
- On the Welcome screen, click the language name (probably "English") next to "In-game objects language:"
- Select your language from the drop-down that appears.
- If your language uses non-European glyphs, it may appear at the bottom of the list.
  - To use these languages, Yafc may need to do a one-time download of a suitable font.
Click "Confirm" if Yafc asks permission to download a font.
  - If you do not wish to have Yafc automatically download a suitable font, click "Select font" in the drop-down, and select a font file that supports your language.

If your language is supported by Factorio but does not appear in the Welcome screen, you can manually force YAFC to use the strings for your language:
- Navigate to `yafc2.config` file located at `%localappdata%\YAFC` (`C:\Users\username\AppData\Local\YAFC`). Open it with a text editor.
- Find the `language` section and replace the value with your language code. Here are examples of language codes:
	- Chinese (Simplified): `zh-CN`
	- Chinese (Traditional): `zh-TW`
	- Korean: `ko`
	- Japanese: `ja`
	- Hebrew: `he`
	- Else: Look into `Factorio/data/base/locale` folder and find the folder with your language.
- If your language uses non-European glyphs, you also need to replace the fonts `Yafc/Data/Roboto-Light.ttf` and `Roboto-Regular.ttf` with fonts that support your language.
You may also use the "Select font" button in the language dropdown on the Welcome screen to change the font.
