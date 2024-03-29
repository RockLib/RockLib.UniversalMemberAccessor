# RockLib.UniversalMemberAccessor Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## 2.0.0 - 2022-09-02

#### Added
- Added `.editorconfig` and `Directory.Build.props` files to ensure consistency.

#### Changed
- Supported targets: net6.0, netcoreapp3.1, and net48.
- As the package now uses nullable reference types, some method parameters now specify if they can accept nullable values.

## 1.0.9 - 2021-08-12

#### Changed

- Changes "Quicken Loans" to "Rocket Mortgage".

## 1.0.8 - 2021-05-10

#### Added

- Adds SourceLink to nuget package.

#### Changed

- Updates System.Reflection.Emit.Lightweight package to latest version.

----

**Note:** Release notes in the above format are not available for earlier versions of
RockLib.Secrets. What follows below are the original release notes.

----

## 1.0.7

Adds net5.0 target

## 1.0.6

Adds icon to project and nuget package.

## 1.0.5

Updates to align with nuget conventions.

## 1.0.4

Fixes a bug where null parameters on implicit generic methods would always fail.

## 1.0.3

Adds support for awaiting generic tasks.

## 1.0.2

Fixes bug when calling generic methods.

## 1.0.1

Fixes two bugs in the static constructor of the UniversalMemberAccessor class.

## 1.0.0

Initial release.
