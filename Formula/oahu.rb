# typed: false
# frozen_string_literal: true

class Oahu < Formula
  desc "Standalone Audible downloader and decrypter"
  homepage "https://github.com/DavidObando/Oahu"
  version "1.0.25"
  license "GPL-3.0-only"

  on_macos do
    on_arm do
      url "https://github.com/DavidObando/Oahu/releases/download/v#{version}/Oahu-#{version}-osx-arm64.tar.gz"
      sha256 "bdbb7117d62dfcb7a58ca0a0610dbe576487097ce1e3f558c8c094b38fc213ca"
    end
    on_intel do
      url "https://github.com/DavidObando/Oahu/releases/download/v#{version}/Oahu-#{version}-osx-x64.tar.gz"
      sha256 "1b45b6e2c0619d9a7b1a63e858f4d70844cf05dab8aac02c8d316347d44f0e0f"
    end
  end

  on_linux do
    on_arm do
      url "https://github.com/DavidObando/Oahu/releases/download/v#{version}/Oahu-#{version}-linux-arm64.tar.gz"
      sha256 "7e322d162ee6f1a241742e8065626cfb0eff801e777cb28bf2ae2dbea38f5bb0"
    end
    on_intel do
      url "https://github.com/DavidObando/Oahu/releases/download/v#{version}/Oahu-#{version}-linux-x64.tar.gz"
      sha256 "f7b66dbc96f35da0afbac082e61be825c19f5fcfa2a614535fac6149c0fbddfb"
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
