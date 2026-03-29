# typed: false
# frozen_string_literal: true

class Oahu < Formula
  desc "Standalone Audible downloader and decrypter"
  homepage "https://github.com/DavidObando/Oahu"
  version "1.0.41"
  license "GPL-3.0-only"

  on_macos do
    on_arm do
      url "https://github.com/DavidObando/Oahu/releases/download/v#{version}/Oahu-#{version}-osx-arm64.tar.gz"
      sha256 "8c9d4281404737cf1d0cda4e93f7bf31af3e411849fc292aeb11a40205f08c3c"
    end
    on_intel do
      url "https://github.com/DavidObando/Oahu/releases/download/v#{version}/Oahu-#{version}-osx-x64.tar.gz"
      sha256 "83b04fc9cffdab6dfc5dd404e4fff38999bc47a3c5e7489c49bcdc2c18c6a1fa"
    end
  end

  on_linux do
    on_arm do
      url "https://github.com/DavidObando/Oahu/releases/download/v#{version}/Oahu-#{version}-linux-arm64.tar.gz"
      sha256 "862946b423e6dff81c956e9e6488a70245418455f12b6b0c2d55014e80acab20"
    end
    on_intel do
      url "https://github.com/DavidObando/Oahu/releases/download/v#{version}/Oahu-#{version}-linux-x64.tar.gz"
      sha256 "87dc82a37b81a1aa0df28f836d4afa86a390e2e410336fbbc4e384f734428dc2"
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
