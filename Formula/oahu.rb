# typed: false
# frozen_string_literal: true

class Oahu < Formula
  desc "Standalone Audible downloader and decrypter"
  homepage "https://github.com/DavidObando/Oahu"
  version "1.0.44"
  license "GPL-3.0-only"

  on_macos do
    on_arm do
      url "https://github.com/DavidObando/Oahu/releases/download/v#{version}/Oahu-#{version}-osx-arm64.tar.gz"
      sha256 "8ff89ec8d66f98b397e2231a7dd43e5bf15699a17661245dc31da8a2c43f8e13"
    end
    on_intel do
      url "https://github.com/DavidObando/Oahu/releases/download/v#{version}/Oahu-#{version}-osx-x64.tar.gz"
      sha256 "97223c98c9f8405f0320b5dfd405cc82020970127b4218eb8d26a2a5f9c14fcb"
    end
  end

  on_linux do
    on_arm do
      url "https://github.com/DavidObando/Oahu/releases/download/v#{version}/Oahu-#{version}-linux-arm64.tar.gz"
      sha256 "af3b57a38185d690d5095f4e2491fe16b023a8dc383dc5f02db654508b9c60c0"
    end
    on_intel do
      url "https://github.com/DavidObando/Oahu/releases/download/v#{version}/Oahu-#{version}-linux-x64.tar.gz"
      sha256 "70d823a5d194a25f04202cd2deb3e18fc2d9ce90652c3245fe206f1aea9c77de"
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
