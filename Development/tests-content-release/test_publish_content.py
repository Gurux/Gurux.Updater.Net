import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'scripts'))
from publish_content import publish


class FakeGitHub:
    """Transport boundary simulator: use the real publisher state machine, never real GitHub writes."""
    def __init__(self, manifest, package, *, published=False, fail_upload=False, fail_dispatch=False):
        self.manifest = manifest
        self.package = package
        self.published = published
        self.created = published
        self.calls = []
        self.fail_upload = fail_upload
        self.fail_dispatch = fail_dispatch

    def run(self, args, **kwargs):
        args = list(map(str, args))
        self.calls.append(args)
        if args[:2] == ['dotnet', 'validator']:
            return subprocess.CompletedProcess(args, 0, '{}', '')
        if args[:2] == ['gh', 'api'] and '/releases/tags/' in args[2]:
            if not self.created:
                return subprocess.CompletedProcess(args, 1, '', 'gh: Not Found (HTTP 404)')
            state = {'draft': not self.published, 'assets': [{'name': name, 'state': 'uploaded'} for name in (self.manifest['package'], 'release-manifest.json')]}
            return subprocess.CompletedProcess(args, 0, json.dumps(state), '')
        if args[:3] == ['gh', 'release', 'create']:
            self.created = True
        elif args[:3] == ['gh', 'release', 'upload'] and self.fail_upload:
            raise RuntimeError('upload failed')
        elif args[:3] == ['gh', 'release', 'edit']:
            self.published = True
        elif args[:3] == ['gh', 'release', 'download']:
            directory = Path(args[args.index('--dir') + 1])
            name = args[args.index('--pattern') + 1]
            if name == 'release-manifest.json':
                (directory / name).write_text(json.dumps(self.manifest), encoding='utf-8')
            else:
                (directory / name).write_bytes(self.package)
        elif args[:4] == ['gh', 'api', '--method', 'POST']:
            if not self.published:
                raise AssertionError('Dispatch preceded publication')
            self.payload = json.loads(kwargs['input'])
            if self.fail_dispatch:
                raise RuntimeError('dispatch failed')
        return subprocess.CompletedProcess(args, 0, '', '')


class PublicationTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.manifest = {'kind': 'localization', 'owner': 'App', 'unit': 'fi', 'version': '1.0.0',
                         'tag': 'localization/App/fi/v1.0.0', 'package': 'App-fi.json'}
        self.manifest_path = self.root / 'release-manifest.json'
        self.manifest_path.write_text(json.dumps(self.manifest), encoding='utf-8')
        self.package_path = self.root / 'App-fi.json'
        self.package_path.write_bytes(b'package')

    def run_publish(self, github):
        publish(github, 'Owner/Source', self.manifest_path, self.package_path, 'validator', 'Owner/Catalog', 'test-token')

    def test_success_publishes_and_verifies_before_dispatch(self):
        github = FakeGitHub(self.manifest, b'package')
        self.run_publish(github)
        self.assertEqual('localization-release', github.payload['event_type'])
        self.assertEqual('localization/App/fi/v1.0.0', github.payload['client_payload']['tag'])
        self.assertEqual(['gh', 'api', '--method', 'POST'], github.calls[-1][:4])
        self.assertFalse(any('--clobber' in call for call in github.calls))

    def test_failed_upload_does_not_notify(self):
        github = FakeGitHub(self.manifest, b'package', fail_upload=True)
        with self.assertRaisesRegex(RuntimeError, 'upload failed'):
            self.run_publish(github)
        self.assertFalse(any(call[:4] == ['gh', 'api', '--method', 'POST'] for call in github.calls))

    def test_published_tag_retry_repeats_notification_without_upload_or_create(self):
        github = FakeGitHub(self.manifest, b'package', published=True)
        self.run_publish(github)
        self.run_publish(github)
        self.assertEqual(2, sum(call[:4] == ['gh', 'api', '--method', 'POST'] for call in github.calls))
        self.assertFalse(any(call[:3] in (['gh', 'release', 'create'], ['gh', 'release', 'upload'], ['gh', 'release', 'edit']) for call in github.calls))

    def test_failed_notification_can_be_retried_on_same_published_tag(self):
        github = FakeGitHub(self.manifest, b'package', fail_dispatch=True)
        with self.assertRaisesRegex(RuntimeError, 'dispatch failed'):
            self.run_publish(github)
        github.fail_dispatch = False
        self.run_publish(github)
        self.assertEqual(1, sum(call[:3] == ['gh', 'release', 'create'] for call in github.calls))
        self.assertEqual(2, sum(call[:3] == ['gh', 'release', 'upload'] for call in github.calls))

    def test_conflicting_published_manifest_never_notifies(self):
        github = FakeGitHub({**self.manifest, 'version': '9.0.0'}, b'package', published=True)
        with self.assertRaisesRegex(RuntimeError, 'conflicts'):
            self.run_publish(github)
        self.assertFalse(any(call[:4] == ['gh', 'api', '--method', 'POST'] for call in github.calls))
