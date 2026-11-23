/* Host test of loader/source/GameSettings.c (run by test-gamesettings.sh). */
#include <stdio.h>
#include <string.h>
#include "GameSettings.h"

static int fails;
static void check(const char *name, int ok) { if (!ok) fails++; printf("%s %s\n", ok ? "PASS" : "FAIL", name); }

static int parse(const char *text, signed char out[GS_COUNT])
{
	char buf[4096];
	strncpy(buf, text, sizeof(buf) - 1);
	buf[sizeof(buf) - 1] = 0;
	return GameSettingsParse(buf, out);
}

int main(void)
{
	signed char s[GS_COUNT];
	int n = parse("[Core]\r\nCPUThread = False\r\nProgressiveScan = True\r\n[Video_Settings]\r\nAspectRatio = 1\r\nwideScreenHack = True\r\nSafeTextureCacheColorSamples = 512\r\n", s);
	check("a Dolphin file: the known keys are read, the others skipped", n == 3 && s[GS_WIIU_WIDE] == 1 && s[GS_FORCE_WIDE] == 1 && s[GS_FORCE_PROG] == 1 && s[GS_SKIP_IPL] == -1);
	parse("AspectRatio = 2", s);
	check("AspectRatio 2 is 4:3, so Wii U Widescreen off", s[GS_WIIU_WIDE] == 0);
	check("AspectRatio 0 leaves it", parse("AspectRatio = 0", s) == 0 && s[GS_WIIU_WIDE] == -1);
	check("AspectRatio 3 (stretch) leaves it", parse("AspectRatio=3", s) == 0 && s[GS_WIIU_WIDE] == -1);
	parse("WIDESCREENHACK = false\nprogressivescan=YES\nSkipIPL = 1", s);
	check("keys and values are not case sensitive, 1 and yes count", s[GS_FORCE_WIDE] == 0 && s[GS_FORCE_PROG] == 1 && s[GS_SKIP_IPL] == 1);
	check("a value that is not a boolean is ignored", parse("wideScreenHack = maybe\nSkipIPL =", s) == 0 && s[GS_FORCE_WIDE] == -1 && s[GS_SKIP_IPL] == -1);
	n = parse("SkipIPL = True\nSkipIPL = False\n", s);
	check("the last value wins and counts once", n == 1 && s[GS_SKIP_IPL] == 0);
	check("comments, sections and lines without = are skipped", parse("# AspectRatio = 1\n; SkipIPL = 1\n[AspectRatio = 1]\njust text\n", s) == 0);
	check("spaces and tabs around key and value are trimmed", parse("  \tSkipIPL \t=\t True \r\n", s) == 1 && s[GS_SKIP_IPL] == 1);
	check("empty text sets nothing", parse("", s) == 0 && s[GS_WIIU_WIDE] == -1);
	check("no final newline is fine", parse("SkipIPL = True", s) == 1);
	{ char b[] = "x"; check("a null text is safe", GameSettingsParse(NULL, s) == 0 && GameSettingsParse(b, s) == 0); }
	n = parse("[Teconmoon]\nPatchPAL50 = True\nProgressiveScan = True\n", s);
	check("PatchPAL50 is read next to ProgressiveScan", n == 2 && s[GS_PATCH_PAL50] == 1 && s[GS_FORCE_PROG] == 1);
	check("PatchPAL50 False and a bad value", parse("PatchPAL50 = no", s) == 1 && s[GS_PATCH_PAL50] == 0 && parse("PatchPAL50 = x", s) == 0 && s[GS_PATCH_PAL50] == -1);
	check("every setting starts as not mentioned", parse("", s) == 0 && s[GS_PATCH_PAL50] == -1 && s[GS_FORCE_PROG] == -1 && GS_COUNT == 5);
	printf(fails ? "%d FAILED\n" : "ALL PASS\n", fails);
	return fails != 0;
}
