import importlib.util
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location('addresses', Path(__file__).parents[1] / 'scripts/check-repository-addresses.py')
addresses = importlib.util.module_from_spec(spec)
spec.loader.exec_module(addresses)


class RepositoryAddressTests(unittest.TestCase):
    def test_migration_requires_updater_and_document_links_to_follow_actual_repository(self):
        source = 'public const string Repository = "new-owner/project";'
        addresses.validate('new-owner/project', source, [('README.md', 'https://github.com/new-owner/project/releases/latest')])
        with self.assertRaises(ValueError):
            addresses.validate('new-owner/project', source.replace('new-owner', 'old-owner'), [])
        for url in ['https://github.com/old-owner/project/releases/latest',
                    'https://api.github.com/repos/old-owner/project/releases/latest']:
            with self.subTest(url=url), self.assertRaises(ValueError):
                addresses.validate('new-owner/project', source, [('README.md', url)])
        addresses.validate('new-owner/project', source, [('docs.md', 'https://github.com/other/tool')])


if __name__ == '__main__':
    unittest.main()
