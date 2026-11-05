# --------------------------------------------------------------------------
# Gurux Ltd
#
# Copyright (c) Gurux Ltd.
#
# This file is part of Gurux Device Framework.
#
# Commit: $Format:%H$
# Date:   $Format:%cI$
#
# This software is available under either of the following licenses:
#
# 1. GNU General Public License, version 2 only (GPL-2.0-only).
#    See the LICENSE file included with this distribution.
#
# 2. A commercial license granted by Gurux Ltd under a separate written
#    agreement. Use under this option requires a valid commercial license
#    and is governed by the terms of that agreement.
#
# If you have not obtained a commercial license from Gurux Ltd,
# use of this software is governed by GPL-2.0-only.
#
# Commercial licensing: https://www.gurux.fi/
#
# SPDX-License-Identifier: GPL-2.0-only OR LicenseRef-Gurux-Commercial
# --------------------------------------------------------------------------

"""Real local git tags + real C# packaging, with only GitHub transport simulated."""
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from urllib.parse import unquote

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'scripts'))
from publish_content import Runner, execute


class LocalGitHub(Runner):
    def __init__(self):
        self.releases = {}
        self.notifications = []
        self.fail_upload = False
        self.rebuilds = 0
        self.fail_manifest = False
        self.uploads = []

    def run(self, args, **kwargs):
        args = list(map(str, args))
        if args[0] == 'dotnet' and 'rebuild' in args:
            self.rebuilds += 1
        if args[0] in ('git', 'dotnet'):
            return super().run(args, **kwargs)
        if args[:2] == ['gh', 'api'] and '/releases/tags/' in args[2]:
            tag = unquote(args[2].split('/releases/tags/')[1])
            if tag not in self.releases:
                return subprocess.CompletedProcess(args, 1, '', 'gh: Not Found (HTTP 404)')
            release = self.releases[tag]
            return subprocess.CompletedProcess(args, 0, json.dumps({'draft': release['draft'], 'assets': [
                {'name': name, 'state': 'uploaded'} for name in release['files']]}), '')
        if args[:3] == ['gh', 'release', 'create']:
            self.releases[args[3]] = {'draft': True, 'files': {}}
        elif args[:3] == ['gh', 'release', 'upload']:
            if self.fail_upload:
                raise RuntimeError('upload failed')
            for filename in args[4:args.index('--repo')]:
                path = Path(filename)
                if self.fail_manifest and path.name == 'release-manifest.json':
                    raise RuntimeError('manifest upload interrupted')
                self.releases[args[3]]['files'][path.name] = path.read_bytes()
                self.uploads.append((args[3], path.name))
        elif args[:3] == ['gh', 'release', 'edit']:
            self.releases[args[3]]['draft'] = False
        elif args[:3] == ['gh', 'release', 'download']:
            name = args[args.index('--pattern') + 1]
            (Path(args[args.index('--dir') + 1]) / name).write_bytes(self.releases[args[3]]['files'][name])
        elif args[:4] == ['gh', 'api', '--method', 'POST']:
            self.notifications.append(json.loads(kwargs['input']))
        else:
            raise AssertionError(args)
        return subprocess.CompletedProcess(args, 0, '', '')


class TagPipelineTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.source = self.root / 'source'
        self.source.mkdir()
        self.github = LocalGitHub()
        candidates = list((Path(__file__).resolve().parents[1] / 'Gurux.Updater.Tool/bin').glob('*/net10.0/Gurux.Updater.Tool.dll'))
        self.validator = str(max(candidates, key=lambda path: path.stat().st_mtime))
        self.git('init', '-b', 'main')
        self.git('config', 'user.name', 'Release Test')
        self.git('config', 'user.email', 'release@example.test')
        remote = self.root / 'remote.git'
        self.github.run(['git', 'init', '--bare', remote])
        self.git('remote', 'add', 'origin', str(remote))
        translations = self.source / 'localization'
        translations.mkdir()
        (translations / 'en.json').write_text('{"A":"Mail"}', encoding='utf-8')
        (translations / 'fi.json').write_text('{"A":"Posti"}', encoding='utf-8')
        resources = self.source / 'resources'
        resources.mkdir()
        (resources / 'default.txt').write_text('resource', encoding='utf-8')
        (self.source / 'content-sources.json').write_text(json.dumps({'sources': [
            {'kind': 'localization', 'directory': 'localization', 'owner': 'App', 'ownerType': 'application', 'defaultCulture': 'en'},
            {'kind': 'resources', 'directory': 'resources', 'owner': 'App', 'ownerType': 'application', 'name': 'templates'}]}), encoding='utf-8')
        self.commit()

    def git(self, *args):
        return self.github.run(['git', *args], cwd=self.source).stdout.strip()

    def commit(self):
        self.git('add', '.')
        self.git('commit', '-m', 'Content source')
        self.git('push', 'origin', 'main')

    def execute(self, tag=None):
        execute(self.source, 'content-sources.json', self.validator, 'Owner/Source', 'Owner/Catalog', 'test-token', tag, self.github)

    def test_changed_unit_only_gets_tag_and_explicit_tag_retry_only_renotifies(self):
        self.execute()
        first_tags = self.git('tag', '--list').splitlines()
        self.assertEqual(['localization/App/en/v1.0.0', 'localization/App/fi/v1.0.0', 'resources/App/templates/v1.0.0'], first_tags)
        self.execute()
        self.assertEqual(first_tags, self.git('tag', '--list').splitlines())
        (self.source / 'localization/fi.json').write_text('{"A":"Kirje"}', encoding='utf-8')
        self.commit()
        self.execute()
        self.assertEqual(set(first_tags) | {'localization/App/fi/v1.0.1'}, set(self.git('tag', '--list').splitlines()))
        count = len(self.github.notifications)
        rebuilds = self.github.rebuilds
        self.execute('localization/App/fi/v1.0.0')
        self.assertEqual(count + 1, len(self.github.notifications))
        self.assertEqual(4, len(self.github.releases))
        self.assertEqual(rebuilds, self.github.rebuilds)
        self.assertEqual('localization/App/fi/v1.0.0', self.github.notifications[-1]['client_payload']['tag'])

    def test_failed_reserved_tag_is_completed_without_extra_version(self):
        self.github.fail_upload = True
        with self.assertRaisesRegex(RuntimeError, 'upload failed'):
            self.execute()
        self.assertEqual(['localization/App/en/v1.0.0'], self.git('tag', '--list').splitlines())
        self.assertEqual([], self.github.notifications)
        self.github.fail_upload = False
        self.execute()
        self.assertEqual(['localization/App/en/v1.0.0', 'localization/App/fi/v1.0.0', 'resources/App/templates/v1.0.0'], self.git('tag', '--list').splitlines())
        self.assertEqual(3, len(self.github.notifications))

    def test_partial_draft_upload_resumes_without_replacing_uploaded_package(self):
        self.github.fail_manifest = True
        with self.assertRaisesRegex(RuntimeError, 'manifest upload interrupted'):
            self.execute()
        self.assertEqual([], self.github.notifications)
        self.github.fail_manifest = False
        self.execute()
        tag = 'localization/App/en/v1.0.0'
        self.assertEqual(1, self.github.uploads.count((tag, 'localization-App-en-1.0.0.json')))
        self.assertEqual(3, len(self.github.releases))
        self.assertEqual(3, len(self.github.notifications))

    def test_missing_dispatch_token_does_not_reserve_new_tags(self):
        with self.assertRaisesRegex(RuntimeError, 'Missing CATALOG_DISPATCH_TOKEN'):
            execute(self.source, 'content-sources.json', self.validator, 'Owner/Source', 'Owner/Catalog', None, runner=self.github)
        self.assertEqual('', self.git('tag', '--list'))
        self.assertEqual({}, self.github.releases)
