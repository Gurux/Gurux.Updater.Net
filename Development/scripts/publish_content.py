"""Tag -> draft/assets -> published Release -> Catalog dispatch. No version/hash rules here."""
import argparse
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import tempfile
from urllib.parse import quote


class Runner:
    def run(self, args, cwd=None, input=None, env=None, check=True):
        result = subprocess.run(list(map(str, args)), cwd=cwd, input=input, env=env,
                                capture_output=True, text=True, encoding='utf-8', check=False)
        if check and result.returncode:
            raise RuntimeError(result.stderr or result.stdout or f'Command failed: {args[0]}')
        return result


def release_state(runner, repository, tag):
    result = runner.run(['gh', 'api', f'repos/{repository}/releases/tags/{quote(tag, safe="")}'], check=False)
    if result.returncode:
        if '(HTTP 404)' in result.stderr:
            return None
        raise RuntimeError(result.stderr)
    return json.loads(result.stdout)


def publish(runner, repository, manifest_path, package_path, validator, catalog_repository, catalog_token):
    """Retry an exact tag: never replace assets of a published Release, always re-notify after verification."""
    manifest_path = Path(manifest_path)
    package_path = Path(package_path) if package_path is not None else None
    manifest = json.loads(manifest_path.read_text(encoding='utf-8'))
    if package_path is not None:
        runner.run(['dotnet', validator, 'content', 'verify', '--manifest', manifest_path, '--package', package_path])
    else:
        runner.run(['dotnet', validator, 'content', 'verify-manifest', '--manifest', manifest_path])
    if not catalog_token:
        raise RuntimeError('Missing CATALOG_DISPATCH_TOKEN')
    tag = manifest['tag']
    state = release_state(runner, repository, tag)
    if (state is None or state['draft']) and package_path is None:
        raise RuntimeError('Unpublished tag requires rebuilding its pinned package before upload')
    if state is None:
        runner.run(['gh', 'release', 'create', tag, '--repo', repository, '--verify-tag', '--draft', '--title', tag, '--notes', 'Independent content release'])
    if state is None or state['draft']:
        assets = {asset['name']: asset for asset in state.get('assets', [])} if state else {}
        for path in (package_path, manifest_path):
            if path.name in assets:
                if assets[path.name].get('state') != 'uploaded':
                    raise RuntimeError('Incomplete draft asset requires cleanup before retrying the same tag')
                with tempfile.TemporaryDirectory(prefix='gurux-draft-asset-') as temporary:
                    runner.run(['gh', 'release', 'download', tag, '--repo', repository, '--pattern', path.name, '--dir', temporary])
                    if (Path(temporary) / path.name).read_bytes() != path.read_bytes():
                        raise RuntimeError('Draft asset conflicts with immutable reserved tag')
            else:
                # No --clobber: a competing publisher cannot replace an asset
                # even if the draft becomes public between our checks.
                runner.run(['gh', 'release', 'upload', tag, path, '--repo', repository])
        runner.run(['gh', 'release', 'edit', tag, '--repo', repository, '--draft=false', '--latest=false'])
    state = release_state(runner, repository, tag)
    if state is None or state['draft'] or state.get('prerelease'):
        raise RuntimeError('Release is not successfully published; Catalog was not notified')
    assets = {asset['name']: asset for asset in state.get('assets', [])}
    for name in (manifest['package'], 'release-manifest.json'):
        if name not in assets or assets[name].get('state') != 'uploaded':
            raise RuntimeError('Published Release is incomplete; Catalog was not notified')
    with tempfile.TemporaryDirectory(prefix='gurux-published-content-') as temporary:
        for name in (manifest['package'], 'release-manifest.json'):
            runner.run(['gh', 'release', 'download', tag, '--repo', repository, '--pattern', name, '--dir', temporary])
        downloaded = Path(temporary)
        if json.loads((downloaded / 'release-manifest.json').read_text(encoding='utf-8')) != manifest:
            raise RuntimeError('Published manifest conflicts with immutable tag')
        runner.run(['dotnet', validator, 'content', 'verify', '--manifest', downloaded / 'release-manifest.json', '--package', downloaded / manifest['package']])
    payload = {'event_type': manifest['kind'] + '-release', 'client_payload': {
        'repository': repository, 'tag': tag, 'kind': manifest['kind'], 'owner': manifest['owner'], 'unit': manifest['unit']}}
    dispatch_env = {**os.environ, 'GH_TOKEN': catalog_token}
    try:
        runner.run(['gh', 'api', '--method', 'POST', f'repos/{catalog_repository}/dispatches', '--input', '-'],
                   input=json.dumps(payload), env=dispatch_env)
    except RuntimeError as error:
        raise RuntimeError(f'Release {tag} is published, but Catalog notification failed: {error}. Retry the same tag; no new version is needed.') from error
    print(f'{tag}: published assets verified; Catalog notified', file=sys.stderr)


def annotation(runner, root, tag):
    if not tag.startswith(('localization/', 'resources/')):
        raise ValueError('Expected localization/<owner>/<culture>/v<version> or resources/<owner>/<name>/v<version>')
    raw = runner.run(['git', 'cat-file', '-p', 'refs/tags/' + tag], cwd=root).stdout
    if '\n\n' not in raw or not raw.startswith('object '):
        raise ValueError('Content tags must be annotated with their immutable release manifest')
    manifest = json.loads(raw.split('\n\n', 1)[1])
    if manifest['tag'] != tag:
        raise ValueError('Tag and manifest identity differ')
    return manifest


def checked_sources(root, configuration):
    path = (root / configuration).resolve()
    if not path.is_relative_to(root):
        raise ValueError('Source configuration must stay inside tagged repository')
    data = json.loads(path.read_text(encoding='utf-8'))
    for source in data['sources']:
        for field in ('directory', 'project', 'metadataPackage'):
            if source.get(field) and not (path.parent / source[field]).resolve().is_relative_to(root):
                raise ValueError('Release inputs must stay inside tagged repository')
    return path


def execute(root, sources, validator, repository, catalog_repository, catalog_token, retry_tag=None, runner=None):
    runner = runner or Runner()
    root = Path(root).resolve()
    for value in (repository, catalog_repository):
        if not re.fullmatch(r'[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+', value):
            raise ValueError('Expected GitHub owner/repository')
    runner.run(['git', 'fetch', 'origin', '--tags'], cwd=root)
    with tempfile.TemporaryDirectory(prefix='gurux-content-release-') as temporary:
        temporary = Path(temporary)
        def retry(tag):
            manifest = annotation(runner, root, tag)
            state = release_state(runner, repository, tag)
            if state is not None and not state['draft']:
                # Notification retry needs the immutable published assets, not a
                # rebuild with a potentially newer SDK/packager implementation.
                published_manifest = temporary / 'release-manifest.json'
                published_manifest.write_text(json.dumps(manifest), encoding='utf-8')
                publish(runner, repository, published_manifest, None, validator, catalog_repository, catalog_token)
                return
            checkout = temporary / 'source'
            runner.run(['git', 'worktree', 'add', '--detach', checkout, tag], cwd=root)
            try:
                manifest_path = temporary / 'expected.json'
                manifest_path.write_text(json.dumps(manifest), encoding='utf-8')
                result = runner.run(['dotnet', validator, 'content', 'rebuild', '--sources', checked_sources(checkout.resolve(), sources),
                                     '--manifest', manifest_path, '--output', temporary / 'rebuilt'])
                item = json.loads(result.stdout)[0]
                publish(runner, repository, item['manifest'], item['package'], validator, catalog_repository, catalog_token)
            finally:
                runner.run(['git', 'worktree', 'remove', '--force', checkout], cwd=root)
        if retry_tag:
            retry(retry_tag)
            return
        tags = runner.run(['git', 'tag', '--list', 'localization/*', 'resources/*'], cwd=root).stdout.splitlines()
        history = []
        for tag in tags:
            history.append(annotation(runner, root, tag))
            state = release_state(runner, repository, tag)
            if state is None or state['draft']:
                retry(tag)  # finish reserved versions before allocating anything else
        history_path = temporary / 'history.json'
        history_path.write_text(json.dumps(history), encoding='utf-8')
        result = runner.run(['dotnet', validator, 'content', 'prepare', '--sources', checked_sources(root, sources), '--history', history_path, '--output', temporary / 'prepared'])
        if result.stderr:
            print(result.stderr, file=sys.stderr, end='')
        prepared = json.loads(result.stdout)
        if prepared and not catalog_token:
            raise RuntimeError('Missing CATALOG_DISPATCH_TOKEN; no new tag was reserved')
        revision = runner.run(['git', 'rev-parse', 'HEAD'], cwd=root).stdout.strip()
        for item in prepared:
            runner.run(['git', '-c', 'user.name=github-actions[bot]', '-c', 'user.email=41898282+github-actions[bot]@users.noreply.github.com',
                        'tag', '-a', item['tag'], revision, '-F', item['manifest']], cwd=root)
            runner.run(['git', 'push', 'origin', 'refs/tags/' + item['tag']], cwd=root)
            print(f'Reserved content tag: {item["tag"]}', file=sys.stderr)
            publish(runner, repository, item['manifest'], item['package'], validator, catalog_repository, catalog_token)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root', default='.')
    parser.add_argument('--sources', default='content-sources.json')
    parser.add_argument('--validator', required=True)
    parser.add_argument('--repository', default=os.environ.get('GITHUB_REPOSITORY'))
    parser.add_argument('--catalog-repository', default='Gurux/Gurux.AMI.Catalog')
    parser.add_argument('--tag', help='Retry an existing tag; never allocate a version')
    args = parser.parse_args()
    execute(args.root, args.sources, str(Path(args.validator).resolve()), args.repository, args.catalog_repository,
            os.environ.get('CATALOG_TOKEN'), args.tag)
