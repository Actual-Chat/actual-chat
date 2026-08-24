import importlib.util
import os
import unittest

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
SCRIPT_PATH = os.path.join(SCRIPT_DIR, "derive-max.py")
SPEC = importlib.util.spec_from_file_location("derive_max", SCRIPT_PATH)
DERIVE_MAX = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(DERIVE_MAX)


class FakeMetrics:
    @staticmethod
    def width(text):
        return len(text)


class DeriveMaxTest(unittest.TestCase):
    def test_visible_text_ignores_markup_placeholders_and_entities(self):
        text = DERIVE_MAX.visible_text("Hello<br><b>{name}</b>&nbsp;world")

        self.assertEqual("Hello\n\u00a0world", text)

    def test_plural_forms_compete_independently(self):
        catalogs = {
            "en": {"Count": "one|the longest plural form"},
            "de": {"Count": "a moderately wide value"},
        }

        values = DERIVE_MAX.derive_values(FakeMetrics(), catalogs, "en")

        self.assertEqual("a moderately wide value", values["Count"])
        self.assertNotIn("|", values["Count"])

    def test_all_date_resources_come_from_one_source(self):
        english = {key: "a|b" for key in DERIVE_MAX.DATE_NAME_KEYS}
        german = {key: "the widest name|short" for key in DERIVE_MAX.DATE_NAME_KEYS}
        english["Date_TimePattern"] = "en-pattern"
        german["Date_TimePattern"] = "de-pattern"
        catalogs = {"en": english, "de": german}

        date_source = DERIVE_MAX.select_date_source(FakeMetrics(), catalogs)
        values = DERIVE_MAX.derive_values(FakeMetrics(), catalogs, date_source)

        self.assertEqual("de", date_source)
        self.assertEqual(german, values)

    def test_checked_in_font_metrics_distinguish_glyph_advances(self):
        root = os.path.dirname(os.path.dirname(SCRIPT_DIR))
        font_path = os.path.join(root, DERIVE_MAX.FONT)
        metrics = DERIVE_MAX.FontMetrics(font_path)

        self.assertGreater(metrics.width("WWW"), metrics.width("iii"))
        self.assertEqual(metrics.width("e"), metrics.width("e\u0301"))
        self.assertGreater(metrics.width("界"), 0)


if __name__ == "__main__":
    unittest.main()
