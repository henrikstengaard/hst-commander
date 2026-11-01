#!/usr/bin/env python3
"""
Makes the third-party notices of Hst Commander (see docs/application.md#licenses):

- THIRD-PARTY-NOTICES.txt at the root of the repository: the third-party components the TUI and GUI include, each with
  a "Packages:" line of its NuGet packages, its copyright notice and license text.
- src/Hst.Commander.Plugins.Smb/THIRD-PARTY-NOTICES.txt: SMBLibrary's notice, the commit of its source and the LGPL and
  GPL version 3 texts.

The packages are read from the restored project.assets.json of the TUI, GUI and SMB plugin, and license texts from the
NuGet packages or from licenses/ (copies of the upstream license files, refreshed with --fetch). Every package the apps
include must be listed in SECTIONS (otherwise the script fails, like GivenLicenses), and listed packages they don't
include are left out. The SMBLibrary commit is read from SMBLIBRARY_COMMIT in .github/workflows/build-and-release.yml.

Usage (restore the solution first, "dotnet restore src/Hst.Commander.sln"):
    python src/notices/make-notices.py           write the notices
    python src/notices/make-notices.py --check   exit with 1 when the notices are outdated
    python src/notices/make-notices.py --fetch   download licenses/ from upstream, then write the notices

Only the Python standard library is used.
"""

import argparse
import fnmatch
import json
import os
import re
import sys
import urllib.request

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
SRC_DIR = os.path.dirname(SCRIPT_DIR)
ROOT_DIR = os.path.dirname(SRC_DIR)
LICENSES_DIR = os.path.join(SCRIPT_DIR, 'licenses')
NOTICES_PATH = os.path.join(ROOT_DIR, 'THIRD-PARTY-NOTICES.txt')
SMB_NOTICES_PATH = os.path.join(SRC_DIR, 'Hst.Commander.Plugins.Smb', 'THIRD-PARTY-NOTICES.txt')
RELEASE_WORKFLOW_PATH = os.path.join(ROOT_DIR, '.github', 'workflows', 'build-and-release.yml')
APPS = {'TUI': 'Hst.Commander.Tui', 'GUI': 'Hst.Commander.Gui'}
SMB_PROJECT = 'Hst.Commander.Plugins.Smb'

# upstream license files copied to licenses/, downloaded again with --fetch
LICENSE_URLS = {
    'avalonia.txt': 'https://raw.githubusercontent.com/AvaloniaUI/Avalonia/master/licence.md',
    'microcom.txt': 'https://raw.githubusercontent.com/kekekeks/MicroCom/master/LICENSE',
    'tmds-dbus.txt': 'https://raw.githubusercontent.com/tmds/Tmds.DBus/main/COPYING',
    'terminal-gui.txt': 'https://raw.githubusercontent.com/gui-cs/Terminal.Gui/v2_develop/LICENSE',
    'colorhelper.txt': 'https://raw.githubusercontent.com/iamartyom/ColorHelper/master/LICENSE',
    'wcwidth.txt': 'https://raw.githubusercontent.com/spectreconsole/wcwidth/main/LICENSE.md',
    'io-abstractions.txt': 'https://raw.githubusercontent.com/TestableIO/System.IO.Abstractions/main/LICENSE',
    'testably.txt': 'https://raw.githubusercontent.com/Testably/Testably.Abstractions/main/LICENSE',
    'jetbrains-annotations.txt': 'https://raw.githubusercontent.com/JetBrains/JetBrains.Annotations/main/license.md',
    'humanizer.txt': 'https://raw.githubusercontent.com/Humanizr/Humanizer/main/license.txt',
    'discutils.txt': 'https://raw.githubusercontent.com/LTRData/DiscUtils/LTRData.DiscUtils-initial/LICENSE.txt',
    'ltrdata-library.txt': 'https://raw.githubusercontent.com/LTRData/Library/master/LICENSE',
    'apache-2.0.txt': 'https://www.apache.org/licenses/LICENSE-2.0.txt',
    'lgpl-3.0.txt': 'https://www.gnu.org/licenses/lgpl-3.0.txt',
    'gpl-3.0.txt': 'https://www.gnu.org/licenses/gpl-3.0.txt',
}

MIT_DOTNET = """The MIT License (MIT)

Copyright (c) .NET Foundation and Contributors

All rights reserved.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE."""


def lic(name):
    """Text of a license file in licenses/."""
    return lambda packages: read_text(os.path.join(LICENSES_DIR, name))


def pkg(package_id, file_name):
    """Text of a file in a NuGet package included with the apps."""
    return lambda packages: read_text(os.path.join(packages[package_id.lower()], file_name))


def join(*parts):
    return lambda packages: '\n\n'.join(part(packages) if callable(part) else part for part in parts)


# components in order: (title, url, license, note on where it's used, package patterns, text). A package belongs to
# the first section with a matching pattern (fnmatch, case-insensitive).
SECTIONS = [
    ('Avalonia', 'https://github.com/AvaloniaUI/Avalonia', 'MIT', None,
     ['Avalonia', 'Avalonia.Desktop', 'Avalonia.FreeDesktop', 'Avalonia.FreeDesktop.AtSpi', 'Avalonia.HarfBuzz',
      'Avalonia.Native', 'Avalonia.Remote.Protocol', 'Avalonia.Skia', 'Avalonia.Themes.Fluent', 'Avalonia.Win32',
      'Avalonia.X11'], lic('avalonia.txt')),
    ('ANGLE (Avalonia.Angle.Windows.Natives)', 'https://chromium.googlesource.com/angle/angle', 'BSD-3-Clause',
     'on Windows', ['Avalonia.Angle.Windows.Natives'], pkg('Avalonia.Angle.Windows.Natives', 'LICENSE')),
    ('MicroCom.Runtime', 'https://github.com/kekekeks/MicroCom', 'MIT', None, ['MicroCom.Runtime'],
     lic('microcom.txt')),
    ('Tmds.DBus.Protocol', 'https://github.com/tmds/Tmds.DBus', 'MIT', 'on Linux', ['Tmds.DBus.Protocol'],
     lic('tmds-dbus.txt')),
    ('HarfBuzzSharp', 'https://github.com/mono/SkiaSharp', 'MIT', None, ['HarfBuzzSharp', 'HarfBuzzSharp.NativeAssets.*'],
     join(pkg('HarfBuzzSharp', 'LICENSE.txt'),
          'The native HarfBuzz library includes third-party material listed in\nTHIRD-PARTY-NOTICES-SKIASHARP.txt.')),
    ('SkiaSharp', 'https://github.com/mono/SkiaSharp', 'MIT', None, ['SkiaSharp', 'SkiaSharp.NativeAssets.*'],
     join(pkg('SkiaSharp', 'LICENSE.txt'),
          'The native Skia library (libSkiaSharp) includes third-party material, e.g. Skia,\n'
          'libpng, zlib, FreeType, libjpeg-turbo and libwebp, listed with their licenses in\n'
          'THIRD-PARTY-NOTICES-SKIASHARP.txt.')),
    ('Terminal.Gui', 'https://github.com/gui-cs/Terminal.Gui', 'MIT', None, ['Terminal.Gui'], lic('terminal-gui.txt')),
    ('ColorHelper', 'https://github.com/iamartyom/ColorHelper', 'MIT', None, ['ColorHelper'], lic('colorhelper.txt')),
    ('Wcwidth', 'https://github.com/spectreconsole/wcwidth', 'MIT', None, ['Wcwidth'], lic('wcwidth.txt')),
    ('System.IO.Abstractions', 'https://github.com/TestableIO/System.IO.Abstractions', 'MIT', None,
     ['System.IO.Abstractions', 'TestableIO.System.IO.Abstractions', 'TestableIO.System.IO.Abstractions.Wrappers'],
     lic('io-abstractions.txt')),
    ('Testably.Abstractions', 'https://github.com/Testably/Testably.Abstractions', 'MIT', None,
     ['Testably.Abstractions.FileSystem.Interface'], lic('testably.txt')),
    ('JetBrains.Annotations', 'https://github.com/JetBrains/JetBrains.Annotations', 'MIT', None,
     ['JetBrains.Annotations'], lic('jetbrains-annotations.txt')),
    ('Humanizer', 'https://github.com/Humanizr/Humanizer', 'MIT', None, ['Humanizer.Core'], lic('humanizer.txt')),
    ('DiscUtils', 'https://github.com/LTRData/DiscUtils', 'MIT', None, ['LTRData.DiscUtils.*'], lic('discutils.txt')),
    ('LTRData.Extensions', 'https://github.com/LTRData/Library', 'MIT', None, ['LTRData.Extensions'],
     lic('ltrdata-library.txt')),
    ('Hst.Amiga', 'https://github.com/henrikstengaard/hst-amiga', 'MIT', None, ['Hst.Amiga'],
     pkg('Hst.Amiga', 'license.txt')),
    ('Hst.Core, Hst.Compression and Hst.Imaging', 'https://github.com/henrikstengaard/hst-core', 'MIT', None,
     ['Hst.Core', 'Hst.Compression', 'Hst.Imaging', 'Hst.Imaging.Pngcs'], pkg('Hst.Core', 'license.txt')),
    ('Pngcs (Hjg.Pngcs)', 'https://github.com/henrikstengaard/pngcs', 'Apache-2.0', None, ['Hjg.Pngcs'],
     join(pkg('Hjg.Pngcs', 'license.txt'), lic('apache-2.0.txt'))),
    # sni.dll, before the other .NET packages matching runtime.*
    ('SQL Server Network Interface (sni.dll)', 'https://github.com/dotnet/corefx', 'MICROSOFT .NET LIBRARY license',
     'on Windows', ['runtime.native.System.Data.SqlClient.sni', 'runtime.*.runtime.native.System.Data.SqlClient.sni'],
     join(pkg('runtime.win-x64.runtime.native.System.Data.SqlClient.sni', 'dotnet_library_license.txt'),
          pkg('runtime.win-x64.runtime.native.System.Data.SqlClient.sni', 'ThirdPartyNotices.txt'))),
    ('.NET libraries', 'https://github.com/dotnet', 'MIT', None, ['Microsoft.*', 'System.*', 'runtime.*'],
     join(MIT_DOTNET,
          'These libraries are part of .NET (https://github.com/dotnet/runtime, https://github.com/dotnet/roslyn,\n'
          'https://github.com/dotnet/wcf and https://github.com/dotnet/maintenance-packages). The third-party\n'
          'material they include is listed in THIRD-PARTY-NOTICES-DOTNET.txt (dotnet/runtime) and below\n'
          '(dotnet/wcf).',
          pkg('System.ServiceModel.Primitives', 'THIRD-PARTY-NOTICES.TXT'))),
]

HEADER = """THIRD-PARTY SOFTWARE NOTICES AND INFORMATION

Hst Commander is licensed under the MIT license (see LICENSE.txt). It includes
the third-party components listed below, each under its own license, whose
copyright notices and license texts follow. The same notices are shown in
Help > About > Licenses.

Other notices included with Hst Commander:
- THIRD-PARTY-NOTICES-SKIASHARP.txt: the third-party material of the native Skia
  and HarfBuzz libraries of SkiaSharp and HarfBuzzSharp.
- THIRD-PARTY-NOTICES-DOTNET.txt: the third-party material of the .NET runtime
  (https://github.com/dotnet/runtime, MIT license), included with the apps as
  they are published self-contained.

Plugins installed separately (e.g. the SMB plugin) include their own notices,
shown in Help > About > Licenses while they are loaded.

Components:"""

RULE = '#' * 80


def read_text(path):
    """Text of a file with \\n line breaks and no trailing spaces, UTF-8 or else Windows-1252 (e.g. Hjg.Pngcs)."""
    with open(path, 'rb') as file:
        data = file.read()
    try:
        text = data.decode('utf-8')
    except UnicodeDecodeError:
        text = data.decode('cp1252')
    text = text.lstrip('﻿').replace('\r\n', '\n').strip('\n')
    return '\n'.join(line.rstrip() for line in text.split('\n'))


def read_assets(project):
    path = os.path.join(SRC_DIR, project, 'obj', 'project.assets.json')
    if not os.path.exists(path):
        sys.exit(f'{path} not found, restore the solution first: dotnet restore src/Hst.Commander.sln')
    with open(path, encoding='utf-8') as file:
        return json.load(file)


def get_package_dirs(assets):
    """Directory of each package by lower case id, in the first package folder holding it."""
    dirs = {}
    for name, library in assets['libraries'].items():
        if library.get('type') != 'package':
            continue
        for folder in assets['packageFolders']:
            path = os.path.join(folder, library['path'])
            if os.path.isdir(path):
                dirs[name.split('/')[0].lower()] = path
                break
    return dirs


def get_included_packages(assets):
    """Packages with runtime or native files in any target ("_._" placeholders aren't files), like GivenLicenses."""
    packages = {}
    for target in assets['targets'].values():
        for name, library in target.items():
            if library.get('type') != 'package':
                continue
            if any(not file.endswith('/_._')
                   for kind in ('runtime', 'native', 'runtimeTargets') for file in library.get(kind, {})):
                package_id, version = name.split('/')
                packages[package_id] = version
    return packages


def find_section(package_id):
    for index, (_, _, _, _, patterns, _) in enumerate(SECTIONS):
        if any(fnmatch.fnmatchcase(package_id.lower(), pattern.lower()) for pattern in patterns):
            return index
    return None


def used_by(apps, note):
    text = ' and '.join(app for app in APPS if app in apps)
    return f'{text} {note}' if note else text


def make_notices():
    package_dirs = {}
    apps_by_package = {}
    for app, project in APPS.items():
        assets = read_assets(project)
        package_dirs.update(get_package_dirs(assets))
        for package_id in get_included_packages(assets):
            apps_by_package.setdefault(package_id, set()).add(app)

    members = [[] for _ in SECTIONS]
    unlisted = []
    for package_id in sorted(apps_by_package, key=str.lower):
        index = find_section(package_id)
        if index is None:
            unlisted.append(package_id)
        else:
            members[index].append(package_id)
    if unlisted:
        sys.exit('Packages included with the apps without a notice, add them to SECTIONS in make-notices.py:\n  ' +
                 '\n  '.join(unlisted))

    sections = []
    for (title, url, license, note, _, text), packages in zip(SECTIONS, members):
        if not packages:
            print(f'Skipping "{title}": none of its packages are included with the apps', file=sys.stderr)
            continue
        apps = set().union(*(apps_by_package[package_id] for package_id in packages))
        sections.append((title, url, license, used_by(apps, note), packages, text(package_dirs)))

    lines = [HEADER]
    lines += [f'- {title} ({license}, {apps}): {url}' for title, url, license, apps, _, _ in sections]
    lines.append('')
    for title, url, license, apps, packages, text in sections:
        lines += [RULE, f'# {title}', f'# {url}', f'# License: {license}. Used by: {apps}.', RULE, '',
                  'Packages: ' + ', '.join(packages), '', text, '']
    return '\n'.join(lines)


def read_workflow_env(name):
    with open(RELEASE_WORKFLOW_PATH, encoding='utf-8') as file:
        match = re.search(rf'^\s*{name}:\s*(\S+)\s*$', file.read(), re.MULTILINE)
    if not match:
        sys.exit(f'{name} not found in {RELEASE_WORKFLOW_PATH}')
    return match.group(1)


def make_smb_notices():
    packages = get_included_packages(read_assets(SMB_PROJECT))
    version = packages.get('SMBLibrary') or sys.exit(f'SMBLibrary not found in the packages of {SMB_PROJECT}')
    if read_workflow_env('SMBLIBRARY_VERSION') != version:
        sys.exit(f'SMBLIBRARY_VERSION in {RELEASE_WORKFLOW_PATH} must be {version}, the version {SMB_PROJECT} uses, '
                 'with SMBLIBRARY_COMMIT the commit of its source')
    commit = read_workflow_env('SMBLIBRARY_COMMIT')
    return f"""THIRD-PARTY SOFTWARE NOTICES AND INFORMATION
Hst Commander SMB plugin (Hst.Commander.Plugins.Smb)

The SMB plugin is licensed under the MIT license, like Hst Commander. It uses the
third-party component below, distributed as a separate file next to the plugin.

{RULE}
# SMBLibrary {version}
# https://github.com/TalAloni/SMBLibrary
# File: SMBLibrary.dll
{RULE}

Copyright (C) Tal Aloni 2014-2026

SMBLibrary is free software: you can redistribute it and/or modify it under the
terms of the GNU Lesser General Public License as published by the Free Software
Foundation, either version 3 of the License, or (at your option) any later
version. The GNU Lesser General Public License version 3 and the GNU General
Public License version 3, which it supplements, are included below.

SMBLibrary is used unmodified, as the separate file SMBLibrary.dll in the
plugin's directory. You may replace it with a modified version of SMBLibrary
that is compatible with version {version}.

Source code of SMBLibrary {version} (commit {commit}):
  https://github.com/TalAloni/SMBLibrary/tree/{commit}
A copy of this source code is included with each release of the plugin as
SMBLibrary-source-{version}.zip.

{RULE}
# GNU LESSER GENERAL PUBLIC LICENSE, Version 3
{RULE}

{read_text(os.path.join(LICENSES_DIR, 'lgpl-3.0.txt'))}

{RULE}
# GNU GENERAL PUBLIC LICENSE, Version 3
{RULE}

{read_text(os.path.join(LICENSES_DIR, 'gpl-3.0.txt'))}
"""


def fetch_licenses():
    for name, url in LICENSE_URLS.items():
        print(f'Downloading {url}')
        with urllib.request.urlopen(url) as response:
            data = response.read()
        with open(os.path.join(LICENSES_DIR, name), 'wb') as file:
            file.write(data)


def read_current(path):
    """Current notices with \\n line breaks, as git may check them out with \\r\\n; None when missing."""
    if not os.path.exists(path):
        return None
    with open(path, encoding='utf-8') as file:
        return file.read().replace('\r\n', '\n')


def main():
    parser = argparse.ArgumentParser(description='Makes the third-party notices of Hst Commander.')
    parser.add_argument('--check', action='store_true', help='exit with 1 when the notices are outdated')
    parser.add_argument('--fetch', action='store_true', help='download licenses/ from upstream first')
    args = parser.parse_args()

    if args.fetch:
        fetch_licenses()

    outdated = []
    for path, text in [(NOTICES_PATH, make_notices()), (SMB_NOTICES_PATH, make_smb_notices())]:
        relative_path = os.path.relpath(path, ROOT_DIR)
        if read_current(path) == text:
            print(f'{relative_path} is up to date')
        elif args.check:
            outdated.append(relative_path)
        else:
            with open(path, 'w', encoding='utf-8', newline='\n') as file:
                file.write(text)
            print(f'Wrote {relative_path}')

    if outdated:
        sys.exit('Outdated, run python src/notices/make-notices.py: ' + ', '.join(outdated))


if __name__ == '__main__':
    main()
