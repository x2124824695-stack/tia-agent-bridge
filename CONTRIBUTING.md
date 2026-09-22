# Contributing

Open an issue with the host version, IDE version and a minimal offline reproduction.
Remove credentials, private project code and machine identifiers from reports.
Use small pull requests, preserve upstream attribution, and distinguish mocked
tests, actual IDE validation and physical PLC tests. Vendor SDKs are not redistributed.

For PLC source changes, write the complete change batch and its dependencies first,
then compile the entire target PLC application once. Apply complete batches of fixes
before the next whole-application compilation; do not compile after each block write.
