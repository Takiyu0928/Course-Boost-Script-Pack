"""Validate clean full and launcher-only release ZIPs without running bundled tools."""
from pathlib import Path
import json
import sys
import zipfile

def verify_full(path):
    with zipfile.ZipFile(path) as archive:
        assert archive.testzip() is None, 'ZIP CRC failure'
        names = set(archive.namelist())
        required = {
            'TimeSaver.exe', 'README.md', 'docs/launcher/accounts.md', 'docs/launcher/path-probe.md',
            'UnipusAI-v3.6-windows-x86-64/UnipusAI.exe',
            'UnipusAI-v3.6-windows-x86-64/Import-Missing-Units.cmd',
            'UnipusAI-v3.6-windows-x86-64/U校园使用说明.md',
            'domooc_free/App/Firefox64/firefox.exe', 'xuexitong_shuake26.10/exe)/task/main.exe',
        }
        assert required <= names, required - names
        assert not any('/backups/' in n or n.endswith(('tool-paths.json', 'state.json', '.log')) for n in names), 'Runtime state included'
        uni = json.loads(archive.read('UnipusAI-v3.6-windows-x86-64/config.json').decode('utf-8-sig'))
        for key in ('username','password','api_key','cookie','authorization','refresh_token','open_id','course_id','class_id','curricula_id','x_annotator_auth_token'):
            assert not uni.get(key), 'Unipus field not empty: ' + key
        xue = json.loads(archive.read('xuexitong_shuake26.10/exe)/task/tool/account_info.json').decode('utf-8-sig'))
        for key in ('phone_number','password','API','cour','file_name'):
            assert not xue.get(key), 'Xuexitong field not empty: ' + key
        forbidden = {'cookies.sqlite','places.sqlite','key4.db','logins.db','logins.json','sessionstore.jsonlz4'}
        assert not any(n.rsplit('/',1)[-1] in forbidden for n in names), 'Browser personal state included'
        assert not any(n.startswith('domooc_free/Profile/storage/default/http') for n in names), 'Website storage included'
        check_exe(archive.read('TimeSaver.exe'))
        print('Full ZIP CRC, clean credentials, runtime exclusions and required contents PASS')

def check_exe(exe):
    assert exe[:2] == b'MZ'
    for label in ('账号配置','探测启动路径','https://github.com/Takiyu0928/Course-Boost-Script-Pack'):
        assert label.encode('utf-16le') in exe, 'Missing EXE feature: ' + label

def verify_update(path):
    with zipfile.ZipFile(path) as archive:
        assert archive.testzip() is None
        names = {n.replace('\\','/') for n in archive.namelist() if not n.endswith('/')}
        assert {'TimeSaver.exe','README.md','docs/launcher/accounts.md','docs/launcher/path-probe.md'} <= names
        assert all(n in ('TimeSaver.exe','README.md') or n.startswith(('docs/','launcher/assets/icon-v1/')) for n in names), 'Unexpected launcher update file'
        check_exe(archive.read('TimeSaver.exe'))
        print('Launcher ZIP CRC, allowed contents and feature strings PASS')

if __name__ == '__main__':
    output = Path(sys.argv[1])
    verify_full(output/'Course-Boost-Script-Pack-v0.1.2-windows-x64-full.zip')
    verify_update(output/'TimeSaver-v0.1.2-launcher-update.zip')
