"""Regression tests for canonical and aliased blueprint lookups."""
import contextlib
import io
import json
from pathlib import Path
import tempfile
import unittest
import zipfile

import check_ap_consistency as checker


def building(name, *aliases):
    return {"BuildingSpec": {}, "TemplateSpec": {
        "TemplateName": name, "BackwardCompatibleTemplateNames": list(aliases)}}


class BlueprintResolutionTests(unittest.TestCase):
    def read(self, blueprints, zipped=False):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            if zipped:
                path = root / "Blueprints.zip"
                with zipfile.ZipFile(path, "w") as archive:
                    for i, blueprint in enumerate(blueprints):
                        archive.writestr(f"Buildings/{i}.blueprint.json", json.dumps(blueprint))
            else:
                path = root
                for i, blueprint in enumerate(blueprints):
                    (root / f"{i}.blueprint.json").write_text(json.dumps(blueprint))
            return checker.read_blueprint_templates(path)

    def check(self, entries, templates):
        with contextlib.redirect_stdout(io.StringIO()):
            return checker.check_templates_resolve(entries, templates)

    def test_zip_and_directory_use_template_spec_not_filename(self):
        data = [building("Discharge.Folktails", "FluidDump.Folktails")]
        expected = {"Discharge.Folktails": "Discharge.Folktails",
                    "FluidDump.Folktails": "Discharge.Folktails"}
        for zipped in (False, True):
            with self.subTest(zipped=zipped):
                self.assertEqual(expected, self.read(data, zipped))

    def test_legacy_entry_resolves_through_alias(self):
        templates = self.read([building("ThrottlingValve.IronTeeth", "Valve.IronTeeth")])
        self.assertTrue(self.check([("Valve", "IronTeeth", "Valve")], templates))

    def test_other_faction_alias_cannot_hide_missing_template(self):
        templates = self.read([building("ThrottlingValve.IronTeeth", "Valve.Folktails")])
        self.assertFalse(self.check([("Valve", "Folktails", "Valve")], templates))

    def test_unresolved_name_fails(self):
        self.assertFalse(self.check([("Missing", "Folktails", "Missing")], {}))

    def test_old_and_new_entries_for_one_building_fail(self):
        templates = self.read([building("Discharge.Folktails", "FluidDump.Folktails")])
        self.assertFalse(self.check([("Discharge", "Folktails", "Discharge"),
                                     ("FluidDump", "Folktails", "Fluid Dump")], templates))

    def test_canonical_name_takes_priority_over_alias(self):
        templates = self.read([building("Old.Folktails"),
                               building("New.Folktails", "Old.Folktails")])
        self.assertEqual("Old.Folktails", templates["Old.Folktails"])

    def test_ambiguous_alias_fails(self):
        with self.assertRaisesRegex(ValueError, "ambiguous blueprint alias"):
            self.read([building("One.Folktails", "Old.Folktails"),
                       building("Two.Folktails", "Old.Folktails")])

    def test_non_building_template_does_not_resolve(self):
        self.assertEqual({}, self.read([{"TemplateSpec": {"TemplateName": "Decoration.Folktails"}}]))


if __name__ == "__main__":
    unittest.main()
