"""Reject updater/documentation addresses that disagree with the actual repository."""
import os
from pathlib import Path
import re
import subprocess

ROOT = Path(__file__).resolve().parents[1]


def validate(expected, source, documents):
    match = re.search(r'const string Repository = "([\w.-]+/[\w.-]+)";', source)
    if not match or match[1].casefold() != expected.casefold():
        raise ValueError(f'Updater repository must match {expected}')
    project = expected.split('/')[1].casefold()
    for filename, content in documents:
        for address in re.finditer(r'https://(?:github\.com/|api\.github\.com/repos/)([\w.-]+/[\w.-]+)', content):
            repository = address[1]
            if repository.split('/')[1].casefold() == project and repository.casefold() != expected.casefold():
                raise ValueError(f'{filename}: stale repository address {repository}')


def main():
    expected = os.environ.get('GITHUB_REPOSITORY')
    if not expected:
        remote = subprocess.check_output(['git', 'remote', 'get-url', 'origin'], cwd=ROOT, text=True).strip()
        match = re.fullmatch(r'(?:git@github\.com:|https://github\.com/)([\w.-]+/[\w.-]+?)(?:\.git)?/?', remote)
        if not match:
            raise ValueError('Cannot establish the canonical GitHub repository from origin')
        expected = match[1]
    files = subprocess.check_output(['git', 'ls-files', '-z'], cwd=ROOT).decode().split('\0')
    documents = []
    for name in files:
        if Path(name).suffix in {'.cs', '.md', '.yml', '.json', '.iss', '.props', '.csproj'}:
            documents.append((name, ROOT.joinpath(name).read_text(encoding='utf-8-sig')))
    validate(expected, ROOT.joinpath('src/Updates/ReleaseUpdater.cs').read_text(encoding='utf-8-sig'), documents)
    print(f'PASS updater and project links match {expected}')


if __name__ == '__main__':
    main()
