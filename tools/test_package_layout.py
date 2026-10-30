"""Package dependency checks; run with Python's standard unittest runner."""
import json
import unittest
import tempfile
import re
import verify_package_consumer
import sync_full_demo
import build_unitypackage
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
UNITY_PROJECT = ROOT / 'Unity'
PACKAGE = UNITY_PROJECT / 'Packages/com.alchemishty.multiplayer-liveops-toolkit'


class PackageLayoutTests(unittest.TestCase):
    def test_full_demo_dependencies_follow_editor_ui_packaging(self):
        self.assertEqual(verify_package_consumer.sample_dependencies('2022.3.33f1'),
                         {'com.unity.textmeshpro': '3.0.6', 'com.unity.ugui': '1.0.0'})
        for version in ['6000.0.1f1', '6000.3.27f1']:
            self.assertEqual(verify_package_consumer.sample_dependencies(version),
                             {'com.unity.ugui': '2.0.0'})

    def test_unitypackage_documentation_links_use_classic_layout(self):
        source = PACKAGE / 'Documentation~/full-demo.md'
        content = build_unitypackage.export_content(Path('Documentation/full-demo.md'), source)
        self.assertIn(b'(../FullDemo/README.md)', content)
        self.assertNotIn(b'(../Samples~/FullDemo/README.md)', content)

    def test_unitypackage_contains_runtime_demo_and_not_project_state(self):
        files = build_unitypackage.export_sources()
        self.assertEqual(files[Path('Runtime/Identity/IdentityModule.cs')], PACKAGE / 'Runtime/Identity/IdentityModule.cs')
        self.assertEqual(files[Path('FullDemo/Scenes/ExampleScene.unity')], PACKAGE / 'Samples~/FullDemo/Scenes/ExampleScene.unity')
        self.assertIn(Path('LICENSE.md'), files)
        self.assertIn(Path('FullDemo/Models/CREDITS.md'), files)
        self.assertIn(Path('Documentation/equipment-visuals.md'), files)
        for destination, source in files.items():
            self.assertNotIn('Tests', destination.parts)
            self.assertNotIn('Library', destination.parts)
            self.assertNotIn('Editor', destination.parts)
            self.assertNotEqual(destination.name, 'package.json')
            self.assertNotIn(source.suffix, {'.pem', '.key', '.sqlite3'})
            if source.suffix in {'.cs', '.asmdef', '.prefab', '.unity'}:
                self.assertEqual(files[Path(str(destination) + '.meta')], Path(str(source) + '.meta'))

    def test_unity_project_is_contained_in_unity_directory(self):
        project = ROOT / 'Unity'
        for folder in ['Assets', 'Packages', 'ProjectSettings']:
            self.assertTrue((project / folder).is_dir(), folder)
            self.assertFalse((ROOT / folder).exists(), folder)
        self.assertTrue((project / 'ProjectSettings/ProjectVersion.txt').is_file())
        ignore = (ROOT / '.gitignore').read_text()
        for folder in ['[Ll]ibrary', '[Tt]emp', '[Oo]bj', '[Ll]ogs', '[Bb]uilds', 'UserSettings']:
            self.assertIn('/Unity/' + folder + '/', ignore)

    def test_manifest_declares_supported_editor_and_only_runtime_dependencies(self):
        manifest = json.loads((PACKAGE / 'package.json').read_text())
        self.assertEqual(manifest['name'], PACKAGE.name)
        self.assertEqual(manifest['unity'], '2022.3')
        self.assertEqual(set(manifest['dependencies']), {'com.unity.netcode.gameobjects', 'com.unity.nuget.newtonsoft-json', 'com.unity.modules.unitywebrequest', 'com.unity.modules.animation'})
        self.assertEqual(manifest['samples'], [{'displayName': 'Full Demo', 'description': 'Complete playable multiplayer demo with character art, cosmetics and TMP UI. Requires TextMeshPro, uGUI, TMP Essentials and the reference backend.', 'path': 'Samples~/FullDemo'}])
        self.assertTrue((PACKAGE / manifest['samples'][0]['path']).is_dir())

    def test_runtime_has_no_demo_dependencies_or_script_copies(self):
        for source in (PACKAGE / 'Runtime').rglob('*.cs'):
            text = source.read_text()
            for forbidden in ['ApiConfig', 'DevAuthBootstrap', 'AccessoryRegistry', 'ClientPlayerAvatars', 'TMPro', 'UnityEngine.UI', 'UnityEditor']:
                self.assertNotIn(forbidden, text, str(source))
        for module in ['Core','Identity','Wallet','Inventory','Shop','Equipment','Achievements','DailyRewards']:
            directory = PACKAGE / 'Runtime' / module
            self.assertTrue(directory.is_dir(), module)
            definition = json.loads(next(directory.glob('*.asmdef')).read_text())
            self.assertTrue(definition['noEngineReferences'], module)
            self.assertFalse((UNITY_PROJECT / 'Assets/Scripts' / module).exists(), module)

    def test_assemblies_never_reference_the_demo_or_sample_tests(self):
        definitions = list((PACKAGE / 'Runtime').rglob('*.asmdef'))
        self.assertGreaterEqual(len(definitions), 11)
        for path in definitions:
            definition = json.loads(path.read_text())
            self.assertNotIn('MultiplayerLiveOpsToolkit', definition.get('references', []), str(path))
            for reference in definition.get('references', []):
                self.assertNotIn('Tests', reference, str(path))

    def test_full_demo_ships_current_authored_assets(self):
        sync_full_demo.synchronize(check=True)

    def test_full_demo_references_ship_with_sample_or_declared_dependencies(self):
        sample = PACKAGE / 'Samples~/FullDemo'
        self.assertTrue((sample / 'Scenes/ExampleScene.unity').is_file())
        self.assertFalse((sample / 'Editor').exists())
        self.assertFalse((UNITY_PROJECT / 'Assets/Editor/SetupExampleScene.cs').exists())
        self.assertFalse((UNITY_PROJECT / 'Assets/Editor/SetupGameStarter.cs').exists())
        self.assertEqual({p.name for p in (PACKAGE / 'Samples~').iterdir()}, {'FullDemo'})
        # TMP Essentials and UPM dependencies retain their original GUIDs on import.
        # Pinned dependency GUIDs: CI runs without a Unity Library/cache.
        known = {
            '0cd44c1031e13a943bb63640046fad76',  # com.unity.ugui@1.0.0/Runtime/UI/Core/Layout/CanvasScaler.cs.meta
            '1aa08ab6e0800fa44ae55d278d1423e3',  # com.unity.ugui@1.0.0/Runtime/UI/Core/ScrollRect.cs.meta
            '2da0c512f12947e489f739169773d7ca',  # com.unity.textmeshpro@3.0.6/Scripts/Runtime/TMP_InputField.cs.meta
            '30649d3a9faa99c48a7b1166b86bf2a0',  # com.unity.ugui@1.0.0/Runtime/UI/Core/Layout/HorizontalLayoutGroup.cs.meta
            '306cc8c2b49d7114eaa3623786fc2126',  # com.unity.ugui@1.0.0/Runtime/UI/Core/Layout/LayoutElement.cs.meta
            '3245ec927659c4140ac4f8d17403cc18',  # com.unity.ugui@1.0.0/Runtime/UI/Core/Layout/ContentSizeFitter.cs.meta
            '3312d7739989d2b4e91e6319e9a96d76',  # com.unity.ugui@1.0.0/Runtime/UI/Core/RectMask2D.cs.meta
            '4e29b1a8efbd4b44bb3f3716e73f07ff',  # com.unity.ugui@1.0.0/Runtime/UI/Core/Button.cs.meta
            '4f231c4fb786f3946a6b90b886c48677',  # com.unity.ugui@1.0.0/Runtime/EventSystem/InputModules/StandaloneInputModule.cs.meta
            '593a2fe42fa9d37498c96f9a383b6521',  # com.unity.netcode.gameobjects@1.9.1/Runtime/Core/NetworkManager.cs.meta
            '59f8146938fff824cb5fd77236b75775',  # com.unity.ugui@1.0.0/Runtime/UI/Core/Layout/VerticalLayoutGroup.cs.meta
            '6960e84d07fb87f47956e7a81d71c4e6',  # com.unity.netcode.gameobjects@1.9.1/Runtime/Transports/UTP/UnityTransport.cs.meta
            '76c392e42b5098c458856cdf6ecaaaa1',  # com.unity.ugui@1.0.0/Runtime/EventSystem/EventSystem.cs.meta
            '8a8695521f0d02e499659fee002a26c2',  # com.unity.ugui@1.0.0/Runtime/UI/Core/Layout/GridLayoutGroup.cs.meta
            '9541d86e2fd84c1d9990edf0852d74ab',  # com.unity.textmeshpro@3.0.6/Scripts/Runtime/TextMeshPro.cs.meta
            'd5a57f767e5e46a458fc5d3c628d0cbb',  # com.unity.netcode.gameobjects@1.9.1/Runtime/Core/NetworkObject.cs.meta
            'dc42784cf147c0c48a680349fa168899',  # com.unity.ugui@1.0.0/Runtime/UI/Core/GraphicRaycaster.cs.meta
            'e651dbb3fbac04af2b8f5abf007ddc23',  # com.unity.netcode.gameobjects@1.9.1/Runtime/Configuration/NetworkPrefabsList.cs.meta
            'f4688fdb7df04437aeb418b961361dc5',  # com.unity.textmeshpro@3.0.6/Scripts/Runtime/TextMeshProUGUI.cs.meta
            'fe87c0e1cc204ed48ad3b37840f39efc',  # com.unity.ugui@1.0.0/Runtime/UI/Core/Image.cs.meta
        }
        for directory in [sample, PACKAGE / 'Runtime', UNITY_PROJECT / 'Assets/TextMesh Pro']:
            for metadata in directory.rglob('*.meta'):
                match = re.search(r'^guid: ([0-9a-f]{32})', metadata.read_text(errors='replace'), re.M)
                if match:
                    known.add(match.group(1))
        for asset in sample.rglob('*'):
            if asset.is_file() and asset.suffix in {'.unity', '.prefab', '.asset', '.controller', '.mat'}:
                for guid in re.findall(r'guid: ([0-9a-f]{32})', asset.read_text(errors='replace')):
                    if not guid.startswith('0000000000000000'):
                        self.assertIn(guid, known, str(asset))

    def test_upgrade_verification_detects_lost_or_changed_consumer_files(self):
        with tempfile.TemporaryDirectory() as directory:
            scene = Path(directory) / 'Consumer.unity'
            scene.write_bytes(b'consumer scene')
            expected = scene.read_bytes()
            verify_package_consumer.verify_preserved_file(scene, expected)
            scene.write_bytes(b'recreated scene')
            with self.assertRaises(RuntimeError):
                verify_package_consumer.verify_preserved_file(scene, expected)
            scene.unlink()
            with self.assertRaises(RuntimeError):
                verify_package_consumer.verify_preserved_file(scene, expected)

    def test_package_contains_no_private_backend_state(self):
        forbidden = {'.sqlite3','.pem','.key','.unitypackage'}
        for path in PACKAGE.rglob('*'):
            self.assertNotIn(path.suffix.lower(), forbidden, str(path))
            self.assertNotEqual(path.name, '.env')
            if path.suffix in {'.cs','.asmdef'} and 'Samples~' not in path.parts:
                self.assertTrue(Path(str(path)+'.meta').exists(), str(path))


if __name__ == '__main__':
    unittest.main()
