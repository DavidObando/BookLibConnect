# typed: false
# frozen_string_literal: true

class Oahu < Formula
  desc "Standalone Audible downloader and decrypter"
  homepage "https://github.com/DavidObando/Oahu"
  version "1.0.27"
  license "GPL-3.0-only"

  on_macos do
    on_arm do
      url "https://github.com/DavidObando/Oahu/releases/download/v#{version}/Oahu-#{version}-osx-arm64.tar.gz"
      sha256 "2eeb1f048947663f508bc413dc0b40a575557d7b9a2e3f8e07d0bf3a4b8e06e0"
    end
    on_intel do
      url "https://github.com/DavidObando/Oahu/releases/download/v#{version}/Oahu-#{version}-osx-x64.tar.gz"
      sha256 "121994192789cab777029292fc8025414d0233d35da41d1aee57cbfc9d545353"
    end
  end

  on_linux do
    on_arm do
      url "https://github.com/DavidObando/Oahu/releases/download/v#{version}/Oahu-#{version}-linux-arm64.tar.gz"
      sha256 "c17839554305333eb3dea8f8a080273b9f3d4538596841033791713bfac97d13"
    end
    on_intel do
      url "https://github.com/DavidObando/Oahu/releases/download/v#{version}/Oahu-#{version}-linux-x64.tar.gz"
      sha256 "6335a2f43b05d2f5736d55ba6abfa555cf538cc4ae98ef5929f70eaea7bb9701"
    end
  end

  def install
    libexec.install Dir["*"]
    chmod 0755, libexec/"Oahu"
    bin.write_exec_script libexec/"Oahu"
  end

  test do
    assert_predicate libexec/"Oahu", :executable?
  end
end
